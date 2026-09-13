"""Explicit isolated live-write test: own temporary part only, never saves user files."""
import argparse
import json
import queue
import subprocess
import threading
import time
import pythoncom
import win32com.client


class Mcp:
    def __init__(self, server, readonly=False, extra_flags=None):
        flags = (['--read-only'] if readonly else []) + (extra_flags or [])
        self.process = subprocess.Popen(['dotnet', server, '--target', '2027', '--disable-toolbaker', *flags],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            text=True, encoding='utf-8', creationflags=subprocess.CREATE_NO_WINDOW)
        self.messages = queue.Queue()
        self.errors = []
        self.counter = 0
        def reader():
            for line in self.process.stdout:
                self.messages.put(json.loads(line))
        def stderr():
            for line in self.process.stderr:
                self.errors.append(line)
        threading.Thread(target=reader, daemon=True).start()
        threading.Thread(target=stderr, daemon=True).start()

    def send(self, method, params, notification=False):
        self.counter += 1
        message = dict(jsonrpc='2.0', method=method, params=params)
        if not notification:
            message['id'] = self.counter
        self.process.stdin.write(json.dumps(message) + '\n')
        self.process.stdin.flush()
        if notification:
            return
        deadline = time.monotonic() + 40
        while True:
            # This test client also owns STA COM proxies. Pump incoming COM messages
            # while awaiting MCP; a plain queue wait can stall Inventor save callbacks.
            pythoncom.PumpWaitingMessages()
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError('MCP response deadline exceeded')
            try:
                result = self.messages.get(timeout=min(0.05, remaining))
            except queue.Empty:
                continue
            if result.get('id') == self.counter:
                if 'error' in result:
                    raise RuntimeError(result['error'])
                return result['result']

    def initialize(self):
        self.send('initialize', dict(protocolVersion='2024-11-05', capabilities={},
            clientInfo=dict(name='inventor-so-atomic-probe', version='1')))
        self.send('notifications/initialized', {}, True)

    def tool(self, tool_name, **arguments):
        result = self.send('tools/call', dict(name=tool_name, arguments=arguments))
        assert not result.get('isError'), result
        return json.loads(next(c['text'] for c in result['content'] if c['type'] == 'text'))

    def close(self):
        self.process.stdin.close()
        try:
            self.process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--server', required=True)
    args = parser.parse_args()
    app = win32com.client.GetActiveObject('Inventor.Application')
    count = app.Documents.Count
    previous = app.ActiveDocument
    prior_ui = app.UserInterfaceManager.UserInteractionDisabled
    part = None
    client = None
    try:
        # Create and clean up only this test-owned document via COM. All tested edits use MCP.
        part = app.Documents.Add(12290, app.FileManager.GetTemplateFile(12290), True)
        part.ComponentDefinition.Parameters.UserParameters.AddByExpression('ProbeWidth', '20 mm', 'mm')
        client = Mcp(args.server)
        client.initialize()
        def width():
            return float(part.ComponentDefinition.Parameters.Item('ProbeWidth').Value)
        def request(preview=False, fail=False):
            data = client.send('resources/read', dict(uri='inventor://active-document'))
            doc = json.loads(data['contents'][0]['text'])
            assert doc['id'] == 'doc_' + part.InternalName, doc
            steps = [dict(command='set_parameter', arguments=dict(name='ProbeWidth', value='25 mm'))]
            if fail:
                steps.append(dict(command='set_parameter', arguments=dict(name='__missing__', value='25 mm')))
            return dict(document_id=doc['id'], expected_revision=doc['revision'], operations=steps, preview=preview)
        names = {t['name'] for t in client.send('tools/list', {})['tools']}
        assert 'inventor_atomic_batch' in names
        assert not names.intersection({'inventor_set_parameter', 'inventor_send_code', 'inventor_export_step'})
        assert abs(width() - 2) < 1e-8
        print('Direct writes hidden from MCP surface (add-in guard tested separately)', flush=True)
        preview = client.tool('inventor_atomic_batch', **request(True))
        assert preview.get('status') == 'preview_rolled_back', preview
        assert abs(width() - 2) < 1e-8
        failed = client.tool('inventor_atomic_batch', **request(fail=True))
        assert failed.get('ok') is False and 'ROLLED_BACK' in failed['error']['message'], failed
        assert abs(width() - 2) < 1e-8
        stale = request()
        stale['expected_revision'] = 'stale'
        rejected = client.tool('inventor_atomic_batch', **stale)
        assert rejected.get('ok') is False and 'STALE_REVISION' in rejected['error']['message'], rejected
        committed = client.tool('inventor_atomic_batch', **request())
        assert committed.get('status') == 'committed', committed
        assert abs(width() - 2.5) < 1e-8
        assert app.UserInterfaceManager.UserInteractionDisabled == prior_ui
        reader = Mcp(args.server, readonly=True)
        try:
            reader.initialize()
            names = {t['name'] for t in reader.send('tools/list', {})['tools']}
            assert not names.intersection({'inventor_atomic_batch', 'inventor_set_parameter', 'inventor_send_code'})
            selection = reader.send('resources/read', dict(uri='inventor://selection'))
            assert 'selection' in json.loads(selection['contents'][0]['text'])
            events = reader.send('resources/read', dict(uri='inventor://events'))
            assert 'events' in json.loads(events['contents'][0]['text'])
            assert abs(width() - 2.5) < 1e-8
        finally:
            reader.close()
        print(json.dumps(dict(mcp_batch='passed', direct_writes_hidden=True, preview_restored=True,
            failure_rolled_back=True, stale_rejected=True, read_only_filter=True,
            selection_and_events_resources=True, committed_mm=width()*10)), flush=True)
    finally:
        if client:
            client.close()
        if part:
            part.Close(True)
        if previous:
            previous.Activate()
    assert app.Documents.Count == count, 'Document count was not restored'
    print('Fixture closed; original document count restored', flush=True)


if __name__ == '__main__':
    main()
