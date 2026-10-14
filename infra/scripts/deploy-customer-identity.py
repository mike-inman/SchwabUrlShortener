#!/usr/bin/env python3
"""Preview or apply the 2026-10-13 customer identity cutover in isolation.

The template is built from the three live Container App definitions so unrelated
P2 resources are outside this deployment. A secure ARM parameter writes the
customer Web secret without putting its value in arguments, output, or Git.
"""
import argparse
import json
import os
import subprocess
import tempfile
from pathlib import Path

SUBSCRIPTION = 'baaf4acd-ff40-43d1-a7ba-6089a2c4f41e'
GROUP = 'rg-schwab-url-shortener'
TENANT = '6adc36e9-dfe5-469f-8aed-6ef83abcdd19'
API_ID = '8911f6b3-365c-4ce5-a0f7-d12b2e4f230a'
WEB_ID = '93f2f816-0a95-41e0-b7df-7ca3c0cce8a0'
INSTANCE = 'https://schwaburlshortener.ciamlogin.com/'
SPECS = [('containerApps', 'ca-shortener-api'),
         ('containerApps', 'ca-shortener-web'),
         ('jobs', 'caj-shortener-migrate')]


def run(args):
    result = subprocess.run(args, text=True, capture_output=True)
    if result.returncode:
        raise RuntimeError(f'{args[0]} failed (exit {result.returncode}); output suppressed')
    return result.stdout


def resource(kind, name):
    url = (f'https://management.azure.com/subscriptions/{SUBSCRIPTION}'
           f'/resourceGroups/{GROUP}/providers/Microsoft.App/{kind}/{name}?api-version=2024-03-01')
    return json.loads(run(['az', 'rest', '--method', 'get', '--url', url, '-o', 'json']))


def clean(value):
    if isinstance(value, dict):
        return {k: clean(v) for k, v in value.items() if v is not None}
    if isinstance(value, list):
        return [clean(v) for v in value]
    return value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--what-if', action='store_true')
    mode.add_argument('--apply', action='store_true')
    args = parser.parse_args()

    account = json.loads(run(['az', 'account', 'show', '-o', 'json']))
    if account.get('id') != SUBSCRIPTION:
        raise RuntimeError('default Azure CLI profile is not on the approved subscription')
    resources = []
    expected = {
        'ca-shortener-api': {
            'Identity__Mode': 'Customer', 'AzureAd__Instance': INSTANCE,
            'AzureAd__TenantId': TENANT, 'AzureAd__ClientId': API_ID},
        'ca-shortener-web': {
            'Identity__Mode': 'Customer', 'AzureAd__Instance': INSTANCE,
            'AzureAd__Authority': f'{INSTANCE}{TENANT}/v2.0',
            'AzureAd__TenantId': TENANT, 'AzureAd__ClientId': WEB_ID,
            'AzureAd__CallbackPath': '/signin-oidc',
            'AzureAd__SignedOutCallbackPath': '/signout-callback-oidc',
            'ShortenerApi__Scope': f'api://{API_ID}/access_as_user'},
        'caj-shortener-migrate': {
            'Identity__Mode': 'Customer', 'AzureAd__Instance': INSTANCE,
            'AzureAd__TenantId': TENANT, 'AzureAd__ClientId': API_ID},
    }
    for kind, name in SPECS:
        live = resource(kind, name)
        properties = clean({k: live['properties'][k] for k in
                            ('environmentId', 'configuration', 'template',
                             'runningStatus', 'workloadProfileName') if k in live['properties']})
        env = properties['template']['containers'][0]['env']
        desired = expected[name]
        env[:] = [e for e in env if e['name'] not in
                  ('AzureAd__Instance', 'AzureAd__Authority', 'AzureAd__TenantId',
                   'AzureAd__ClientId', 'AzureAd__CallbackPath',
                   'AzureAd__SignedOutCallbackPath', 'Identity__Mode',
                   'ShortenerApi__Scope')]
        env.extend({'name': k, 'value': v} for k, v in desired.items())
        resources.append({'type': f'Microsoft.App/{kind}', 'apiVersion': '2024-03-01',
                          'name': name, 'location': live['location'], 'tags': live['tags'],
                          'identity': clean(live['identity']), 'properties': properties})

    vaults = json.loads(run(['az', 'keyvault', 'list', '-g', GROUP,
                             '--query', "[?starts_with(name, 'kv-shortener-')].name", '-o', 'json']))
    if len(vaults) != 1:
        raise RuntimeError('expected exactly one shortener Key Vault in the approved group')
    resources.append({'type': 'Microsoft.KeyVault/vaults/secrets',
                      'apiVersion': '2023-07-01', 'name': vaults[0] + '/web-client-secret',
                      'properties': {'value': '[parameters(\'webClientSecret\')]',
                                     'contentType': 'external-id-web-client-secret-2026-10-13'}})
    template = {'$schema': 'https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#',
                'contentVersion': '1.0.0.0',
                'parameters': {'webClientSecret': {'type': 'secureString'}},
                'resources': resources}

    secret = run(['azd', 'env', 'get-value', 'WEB_CLIENT_SECRET', '-e', 'prod']).strip().splitlines()[-1]
    if not secret:
        raise RuntimeError('prod WEB_CLIENT_SECRET is empty')
    with tempfile.TemporaryDirectory(prefix='shortener-identity-') as directory:
        os.chmod(directory, 0o700)
        template_file = Path(directory) / 'template.json'
        params_file = Path(directory) / 'parameters.json'
        template_file.write_text(json.dumps(template))
        params_file.write_text(json.dumps({'parameters': {'webClientSecret': {'value': secret}}}))
        os.chmod(template_file, 0o600)
        os.chmod(params_file, 0o600)
        operation = 'what-if' if args.what_if else 'create'
        command = ['az', 'deployment', 'group', operation, '-g', GROUP,
                   '--subscription', SUBSCRIPTION, '--name', 'shortener-customer-identity-20261013',
                   '--template-file', str(template_file), '--parameters', '@' + str(params_file),
                   '-o', 'json']
        if args.what_if:
            command.append('--no-pretty-print')
        output = run(command)
    result = json.loads(output)
    if args.what_if:
        for change in result.get('changes', []):
            if change['changeType'] == 'Ignore':
                continue
            rid = change['resourceId'].split('/providers/')[-1]
            print(change['changeType'], rid)
            for delta in change.get('delta') or []:
                print(' ', delta['path'], delta['propertyChangeType'])
    else:
        print('Focused customer identity deployment completed; secret value suppressed.')


if __name__ == '__main__':
    main()
