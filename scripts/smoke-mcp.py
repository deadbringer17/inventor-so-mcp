"""Read-only MCP end-to-end probe. No CAD writes, save, close or application quit."""
import argparse
import json
import queue
import subprocess
import threading


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--server', required=True, help='Path to Inventor.So.Mcp.Server.dll')
    parser.add_argument('--live', action='store_true', help='Require a connected Inventor 2027 add-in')
    args = parser.parse_args()
    process = subprocess.Popen(
        ['dotnet', args.server, '--target', '2027', '--read-only', '--disable-toolbaker'],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        text=True, encoding='utf-8', creationflags=subprocess.CREATE_NO_WINDOW,
    )
    messages = queue.Queue()
    errors = []

    def read_stdout():
        for line in process.stdout:
            try:
                messages.put(json.loads(line))
            except Exception as exc:
                messages.put(exc)

    def read_stderr():
        for line in process.stderr:
            errors.append(line)

    threading.Thread(target=read_stdout, daemon=True).start()
    threading.Thread(target=read_stderr, daemon=True).start()
    request_id = 0

    def send(method, params, notification=False):
        nonlocal request_id
        message = dict(jsonrpc='2.0', method=method, params=params)
        if not notification:
            request_id += 1
            message['id'] = request_id
        process.stdin.write(json.dumps(message) + '\n')
        process.stdin.flush()
        if notification:
            return
        while True:
            result = messages.get(timeout=40)
            if isinstance(result, Exception):
                raise result
            if result.get('id') != request_id:
                continue
            if 'error' in result:
                raise RuntimeError(result['error'])
            return result['result']

    try:
        init = send('initialize', dict(protocolVersion='2024-11-05', capabilities={}, clientInfo=dict(name='inventor-so-smoke', version='1')))
        send('notifications/initialized', {}, notification=True)
        tools = send('tools/list', {})['tools']
        names = [t['name'] for t in tools]
        assert 'inventor_extrude' not in names and 'inventor_send_code' not in names
        resources = send('resources/list', {})['resources']
        assert any(r['uri'] == 'inventor://active-document' for r in resources)
        result = dict(protocol=init['protocolVersion'], read_only_tools=names, resources=[r['uri'] for r in resources])
        if args.live:
            health = send('tools/call', dict(name='inventor_health', arguments={}))
            assert not health.get('isError'), health
            data = json.loads(next(c['text'] for c in health['content'] if c['type'] == 'text'))
            assert data.get('ok') is not False, data
            assert data.get('inventor_year') == 2027, data
            result['health'] = data
            doc = send('resources/read', dict(uri='inventor://active-document'))
            document = json.loads(doc['contents'][0]['text'])
            assert document.get('ok') is not False, document
            result['active_document'] = document
            selection = send('resources/read', dict(uri='inventor://selection'))
            result['selection'] = json.loads(selection['contents'][0]['text'])
            assert 'selection' in result['selection']
        print(json.dumps(result, ensure_ascii=False, indent=2))
    finally:
        process.stdin.close()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
        if process.returncode:
            print('Server stderr:', ''.join(errors))


if __name__ == '__main__':
    main()
