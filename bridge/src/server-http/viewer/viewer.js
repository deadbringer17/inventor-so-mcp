// Inventor SO WebXR viewer (plan §29/F29): the Quest MVP runs in the headset browser.
// Flow: connect (MCP over Streamable HTTP) -> scene graph -> per-definition GLB assets (cached by
// content id, so only changed geometry is downloaded again) -> pick a face -> plan/preview/commit a
// parameter change -> reload on the next visual revision. No data is stored beyond this tab.
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { VRButton } from 'three/addons/webxr/VRButton.js';

const $ = (id) => document.getElementById(id);
const ui = {
  token: $('token'), connect: $('connect'), load: $('load'), revision: $('revision'), pick: $('pick'),
  param: $('param'), value: $('value'), preview: $('preview'), commit: $('commit'), status: $('status'),
};

function status(text, kind = '') { ui.status.textContent = text; ui.status.className = kind; }

// ---------------- MCP over Streamable HTTP ----------------
class Mcp {
  constructor(token) { this.token = token; this.session = null; this.nextId = 1; }

  headers(extra = {}) {
    const h = { 'Authorization': 'Bearer ' + this.token, 'Content-Type': 'application/json',
      'Accept': 'application/json, text/event-stream', ...extra };
    if (this.session) h['Mcp-Session-Id'] = this.session;
    return h;
  }

  async post(message) {
    const response = await fetch('/mcp', { method: 'POST', headers: this.headers(), body: JSON.stringify(message) });
    if (response.status === 401) throw new Error('UNAUTHORIZED: check the token.');
    if (response.status === 429) throw new Error('RATE_LIMITED: slow down.');
    const session = response.headers.get('mcp-session-id');
    if (session) this.session = session;
    if (message.id === undefined) return null;
    const type = response.headers.get('content-type') || '';
    const text = await response.text();
    const messages = type.includes('text/event-stream')
      ? text.split(/\r?\n/).filter((l) => l.startsWith('data:')).map((l) => JSON.parse(l.slice(5)))
      : [JSON.parse(text)];
    const reply = messages.find((m) => m.id === message.id);
    if (!reply) throw new Error('No reply to request ' + message.id);
    if (reply.error) throw new Error(reply.error.message || 'MCP error');
    return reply.result;
  }

  async initialize() {
    await this.post({ jsonrpc: '2.0', id: this.nextId++, method: 'initialize', params: {
      protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'inventor-so-webxr-viewer', version: '0.1.0' } } });
    await this.post({ jsonrpc: '2.0', method: 'notifications/initialized' });
  }

  /** Call a tool and return its JSON payload; tool-level errors ({ok:false}) throw with their code. */
  async tool(name, args = {}) {
    const result = await this.post({ jsonrpc: '2.0', id: this.nextId++, method: 'tools/call', params: { name, arguments: args } });
    const text = (result.content || []).find((c) => c.type === 'text')?.text ?? '{}';
    let data;
    try { data = JSON.parse(text); } catch { throw new Error(text); }
    if (data && data.ok === false) {
      const e = new Error((data.error?.code || 'ERROR') + ': ' + (data.error?.message || ''));
      e.code = data.error?.code;
      throw e;
    }
    return data;
  }

  async subscribe(uri) {
    await this.post({ jsonrpc: '2.0', id: this.nextId++, method: 'resources/subscribe', params: { uri } });
  }

  /** Long-lived GET stream for server notifications; calls onUpdate(uri) per resources/updated. */
  async listen(onUpdate) {
    for (;;) {
      try {
        const response = await fetch('/mcp', { method: 'GET', headers: this.headers({ 'Accept': 'text/event-stream' }) });
        if (!response.ok || !response.body) { await sleep(5000); continue; }
        const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
        let buffer = '';
        for (;;) {
          const { value, done } = await reader.read();
          if (done) break;
          buffer += value;
          let cut;
          while ((cut = buffer.indexOf('\n\n')) >= 0) {
            const block = buffer.slice(0, cut); buffer = buffer.slice(cut + 2);
            for (const line of block.split('\n')) {
              if (!line.startsWith('data:')) continue;
              try {
                const msg = JSON.parse(line.slice(5));
                if (msg.method === 'notifications/resources/updated') onUpdate(msg.params?.uri);
              } catch { /* ignore keep-alives */ }
            }
          }
        }
      } catch { /* network drop: reconnect */ }
      await sleep(2000);
    }
  }

  async asset(url) {
    const response = await fetch(url, { headers: { 'Authorization': 'Bearer ' + this.token } });
    if (!response.ok) throw new Error('ASSET_NOT_FOUND: ' + response.status);
    return response.arrayBuffer();
  }
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// ---------------- three.js scene ----------------
const stage = $('stage');
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(window.devicePixelRatio);
renderer.xr.enabled = true;
stage.appendChild(renderer.domElement);
document.body.appendChild(VRButton.createButton(renderer));

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x202428);
scene.add(new THREE.HemisphereLight(0xffffff, 0x444444, 2.2));
const sun = new THREE.DirectionalLight(0xffffff, 1.6); sun.position.set(1, 2, 1.5); scene.add(sun);
const camera = new THREE.PerspectiveCamera(50, 1, 0.001, 100);
camera.position.set(0.4, 0.3, 0.6);
const controls = new OrbitControls(camera, renderer.domElement);
// In VR the CAD model (metres, real scale) sits in front of the user at table height.
const modelRoot = new THREE.Group(); modelRoot.position.set(0, 1.0, -0.6); scene.add(modelRoot);
const highlight = new THREE.MeshBasicMaterial({ color: 0xff9f1a, depthTest: false, transparent: true, opacity: 0.85 });
let highlightMesh = null;

function resize() {
  const w = stage.clientWidth || window.innerWidth, h = stage.clientHeight || window.innerHeight;
  renderer.setSize(w, h); camera.aspect = w / h; camera.updateProjectionMatrix();
}
window.addEventListener('resize', resize); resize();
renderer.setAnimationLoop(() => { controls.update(); renderer.render(scene, camera); });

// ---------------- state ----------------
let mcp = null;
let current = null;                       // { documentId, revision, visualRevision, kind }
const definitionCache = new Map();        // asset_id -> THREE.Group (template)
let plan = null;

const loader = new GLTFLoader();
async function definitionObject(assetId, url) {
  if (!definitionCache.has(assetId)) {
    const gltf = await loader.parseAsync(await mcp.asset(url), '');
    definitionCache.set(assetId, gltf.scene);
  }
  return definitionCache.get(assetId).clone(true);
}

/** Build the three.js tree from the scene graph: leaves instance cached definition meshes. */
async function buildScene(graph) {
  const byDefinition = new Map((graph.definitions || []).map((d) => [d.definition_document_id, d]));
  const group = new THREE.Group();
  async function visit(node, parent, hidden) {
    const skip = hidden || node.suppressed || node.visible === false;
    const children = node.children || [];
    if (children.length === 0) {
      const def = byDefinition.get(node.definition_document_id);
      if (!skip && def) {
        const object = await definitionObject(def.mesh_asset_id, def.asset.asset_url);
        if (node.matrix_gltf) { object.matrixAutoUpdate = false; object.matrix.fromArray(node.matrix_gltf); }
        object.userData.occurrence_id = node.occurrence_id || null;
        object.userData.definition_document_id = node.definition_document_id;
        parent.add(object);
      }
      return;
    }
    for (const child of children) await visit(child, parent, skip);
  }
  await visit(graph.root, group, false);
  // Drop cached definitions no longer used by the scene.
  const used = new Set([...byDefinition.values()].map((d) => d.mesh_asset_id));
  for (const id of [...definitionCache.keys()]) if (!used.has(id)) definitionCache.delete(id);
  return group;
}

async function loadModel() {
  status('Loading scene…');
  const info = await mcp.tool('inventor_get_document_info');
  const graph = await mcp.tool('inventor_get_scene_graph', { include_meshes: true });
  const group = await buildScene(graph);
  modelRoot.clear(); modelRoot.add(group);
  clearHighlight();
  current = { documentId: info.id, revision: info.revision, visualRevision: graph.visual_revision, kind: graph.kind };
  ui.revision.textContent = 'revision ' + current.revision + ' · visual ' + (current.visualRevision ?? '?');
  frame(group);
  await loadParameters();
  status('Loaded ' + (graph.definitions || []).length + ' definition(s).', 'ok');
}

function frame(object) {
  const box = new THREE.Box3().setFromObject(object);
  if (box.isEmpty()) return;
  const size = box.getSize(new THREE.Vector3()).length();
  const center = box.getCenter(new THREE.Vector3());
  controls.target.copy(center);
  camera.position.copy(center).add(new THREE.Vector3(size, size * 0.7, size));
  camera.near = size / 1000; camera.far = size * 100; camera.updateProjectionMatrix();
}

async function loadParameters() {
  try {
    const params = await mcp.tool('inventor_list_parameters');
    const list = params.parameters || params.items || params;
    ui.param.innerHTML = '';
    for (const p of (Array.isArray(list) ? list : [])) {
      const option = document.createElement('option');
      option.value = p.name; option.textContent = p.name + ' = ' + (p.expression ?? p.value ?? '');
      ui.param.appendChild(option);
    }
    const enabled = ui.param.options.length > 0 && current.kind === 'part';
    ui.param.disabled = ui.value.disabled = ui.preview.disabled = !enabled;
  } catch (e) { ui.param.disabled = true; }
}

// ---------------- picking ----------------
const raycaster = new THREE.Raycaster();
function faceOf(hit) {
  const faces = hit.object.geometry?.userData?.faces;
  if (!faces || hit.faceIndex == null) return null;
  const index = hit.faceIndex * 3;
  let lo = 0, hi = faces.length - 1;
  while (lo <= hi) {                       // ranges are sorted by first_index
    const mid = (lo + hi) >> 1, f = faces[mid];
    if (index < f.first_index) hi = mid - 1;
    else if (index >= f.first_index + f.index_count) lo = mid + 1;
    else return f;
  }
  return null;
}

function occurrenceOf(object) {
  for (let o = object; o; o = o.parent) if (o.userData && 'occurrence_id' in o.userData) return o.userData.occurrence_id;
  return null;
}

function clearHighlight() { if (highlightMesh) { highlightMesh.parent?.remove(highlightMesh); highlightMesh = null; } }

function showHighlight(hit, face) {
  clearHighlight();
  const g = hit.object.geometry;
  const sub = new THREE.BufferGeometry();
  sub.setAttribute('position', g.getAttribute('position'));
  sub.setIndex(Array.from(g.index.array.slice(face.first_index, face.first_index + face.index_count)));
  highlightMesh = new THREE.Mesh(sub, highlight);
  highlightMesh.renderOrder = 10;
  hit.object.add(highlightMesh);
}

async function pick(ray) {
  if (!current) return;
  raycaster.ray.copy(ray);
  const hit = raycaster.intersectObject(modelRoot, true).find((h) => h.object.isMesh && h.object !== highlightMesh);
  if (!hit) return;
  const face = faceOf(hit);
  if (!face) { ui.pick.textContent = 'No face data on this mesh.'; return; }
  showHighlight(hit, face);
  const occurrence = occurrenceOf(hit.object);
  try {
    let entity = face.face_id;
    if (current.kind === 'assembly' && occurrence && face.face_id) entity = (await mcp.tool('inventor_pick_entity', { occurrence_id: occurrence, face_id: face.face_id })).entity_id;
    ui.pick.textContent = 'face #' + face.ordinal + (occurrence ? ' in occurrence' : '') + '\n' + (entity || '(no persistent id)');
    if (entity) await mcp.tool('inventor_highlight_entity', { entity_ids: [entity], mode: 'highlight' });
  } catch (e) { status(e.message, 'bad'); }
}

renderer.domElement.addEventListener('pointerup', (event) => {
  if (renderer.xr.isPresenting) return;
  const rect = renderer.domElement.getBoundingClientRect();
  const ndc = new THREE.Vector2(((event.clientX - rect.left) / rect.width) * 2 - 1, -((event.clientY - rect.top) / rect.height) * 2 + 1);
  raycaster.setFromCamera(ndc, camera);
  pick(raycaster.ray.clone());
});

for (const i of [0, 1]) {
  const controller = renderer.xr.getController(i);
  controller.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3(0, 0, -1)]),
    new THREE.LineBasicMaterial({ color: 0x4aa3ff })));
  controller.addEventListener('select', () => {
    const matrix = new THREE.Matrix4().extractRotation(controller.matrixWorld);
    const ray = new THREE.Ray(new THREE.Vector3().setFromMatrixPosition(controller.matrixWorld),
      new THREE.Vector3(0, 0, -1).applyMatrix4(matrix).normalize());
    pick(ray);
  });
  scene.add(controller);
}

// ---------------- plan / preview / commit ----------------
ui.preview.addEventListener('click', async () => {
  if (!current) return;
  try {
    status('Previewing…');
    plan = await mcp.tool('inventor_plan_change', {
      document_id: current.documentId, expected_revision: current.revision,
      operations: [{ command: 'set_parameter', arguments: { name: ui.param.value, value: ui.value.value } }],
      intent: 'Set ' + ui.param.value + ' to ' + ui.value.value + ' from the XR viewer',
    });
    status('Preview OK (rolled back). Plan ' + plan.plan_id + '\nCommit to apply.', 'ok');
    ui.commit.disabled = false;
  } catch (e) {
    plan = null; ui.commit.disabled = true;
    status(e.message + (e.code === 'STALE_REVISION' ? '\nThe model changed: reload it.' : ''), 'bad');
  }
});

ui.commit.addEventListener('click', async () => {
  if (!plan) return;
  ui.commit.disabled = true;
  try {
    status('Committing…');
    const result = await mcp.tool('inventor_commit_plan', { plan_id: plan.plan_id, document_id: current.documentId, expected_revision: current.revision });
    status('Committed. New revision ' + result.revision + '. Reloading changed geometry…', 'ok');
    plan = null;
    await loadModel();
  } catch (e) {
    plan = null;
    status(e.message + (e.code === 'PLAN_MISMATCH' || e.code === 'STALE_REVISION' ? '\nPlan again after reloading.' : '') +
      (e.code === 'ROLLED_BACK' ? '\nNothing was changed.' : ''), 'bad');
  }
});

// ---------------- connection ----------------
try { ui.token.value = sessionStorage.getItem('inventor-so-token') || ''; } catch { /* storage unavailable */ }

ui.connect.addEventListener('click', async () => {
  try {
    status('Connecting…');
    try { sessionStorage.setItem('inventor-so-token', ui.token.value); } catch { /* storage unavailable */ }
    mcp = new Mcp(ui.token.value.trim());
    await mcp.initialize();
    const caps = await mcp.tool('inventor_get_capabilities');
    const ready = caps.capabilities?.xr_mesh && caps.capabilities?.scene_graph;
    ui.load.disabled = !ready;
    status(ready ? 'Connected to Inventor ' + (caps.target?.inventor_year ?? '?') + '.'
      : 'Connected, but XR meshes are unavailable: ' + (caps.target?.reason || 'enable the experimental tier on server and add-in.'), ready ? 'ok' : 'bad');
    if (ready) {
      try { await mcp.subscribe('inventor://events'); } catch { /* subscriptions optional */ }
      mcp.listen(async () => {
        if (!current || plan) return;
        try {
          const v = await mcp.tool('inventor_get_visual_revision', { document_id: current.documentId });
          if (v.visual_revision !== current.visualRevision) await loadModel();
        } catch { /* document closed or switched: user reloads */ }
      });
    }
  } catch (e) { status(e.message, 'bad'); }
});

ui.load.addEventListener('click', () => loadModel().catch((e) => status(e.message, 'bad')));
