#!/usr/bin/env python3
"""Preview or apply only the Track G secret reference on the existing API app."""
import argparse
import json
import os
import subprocess
import tempfile
from pathlib import Path

SUBSCRIPTION = 'baaf4acd-ff40-43d1-a7ba-6089a2c4f41e'
GROUP = 'rg-schwab-url-shortener'
NAME = 'ca-shortener-api'
SECRET = 'visitor-hash-secret'
VAULT = 'kv-shortener-f2xpl6'
IDENTITY = (f'/subscriptions/{SUBSCRIPTION}/resourceGroups/{GROUP}'
            '/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-shortener-api')


def run(args):
    result = subprocess.run(args, text=True, capture_output=True)
    if result.returncode:
        raise RuntimeError(f'{args[0]} failed (exit {result.returncode}); output suppressed')
    return result.stdout


def clean(value):
    if isinstance(value, dict):
        return {key: clean(item) for key, item in value.items() if item is not None}
    if isinstance(value, list):
        return [clean(item) for item in value]
    return value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--what-if', action='store_true')
    mode.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    account = json.loads(run(['az', 'account', 'show', '-o', 'json']))
    if account['id'] != SUBSCRIPTION or account['user']['name'].lower() != 'social@mediaraq.com':
        raise RuntimeError('Azure account does not match the approved operator and subscription')
    url = (f'https://management.azure.com/subscriptions/{SUBSCRIPTION}/resourceGroups/{GROUP}'
           f'/providers/Microsoft.App/containerApps/{NAME}?api-version=2024-03-01')
    live = json.loads(run(['az', 'rest', '--method', 'get', '--url', url, '-o', 'json']))
    properties = clean({key: live['properties'][key] for key in
                        ('environmentId', 'configuration', 'template', 'runningStatus', 'workloadProfileName')
                        if key in live['properties']})
    if IDENTITY.lower() not in {key.lower() for key in live['identity']['userAssignedIdentities']}:
        raise RuntimeError('API managed identity differs from the expected identity')
    secrets = properties['configuration']['secrets']
    reference = {'name': SECRET, 'keyVaultUrl': f'https://{VAULT}.vault.azure.net/secrets/{SECRET}',
                 'identity': IDENTITY}
    current = [item for item in secrets if item['name'] == SECRET]
    if current and current != [reference]:
        raise RuntimeError('API already has a different visitor-hash-secret reference')
    if not current:
        secrets.append(reference)
    env = properties['template']['containers'][0]['env']
    setting = {'name': 'VisitorHash__DailySecret', 'secretRef': SECRET}
    current = [item for item in env if item['name'] == setting['name']]
    if current and current != [setting]:
        raise RuntimeError('API already has a different visitor hash setting')
    if not current:
        env.append(setting)
    template = {
        '$schema': 'https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#',
        'contentVersion': '1.0.0.0',
        'resources': [{
            'type': 'Microsoft.App/containerApps', 'apiVersion': '2024-03-01',
            'name': NAME, 'location': live['location'], 'tags': live['tags'],
            'identity': clean(live['identity']), 'properties': properties,
        }],
    }
    with tempfile.TemporaryDirectory(prefix='shortener-visitor-hash-') as directory:
        os.chmod(directory, 0o700)
        path = Path(directory) / 'template.json'
        path.write_text(json.dumps(template))
        os.chmod(path, 0o600)
        command = ['az', 'deployment', 'group', 'what-if' if args.what_if else 'create',
                   '-g', GROUP, '--subscription', SUBSCRIPTION,
                   '--name', 'shortener-visitor-hash-api-20261014',
                   '--template-file', str(path), '-o', 'json']
        if args.what_if:
            command.append('--no-pretty-print')
        result = json.loads(run(command))
    if args.what_if:
        for item in result.get('changes', []):
            if item['changeType'] != 'Ignore':
                print(item['changeType'], item['resourceId'].split('/providers/')[-1],
                      [(delta['path'], delta['propertyChangeType']) for delta in item.get('delta') or []
                       if delta['propertyChangeType'] != 'NoEffect'])
    else:
        print('Focused API visitor-hash reference deployment completed.')


if __name__ == '__main__':
    main()
