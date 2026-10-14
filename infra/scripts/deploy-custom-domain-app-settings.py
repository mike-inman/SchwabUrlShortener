#!/usr/bin/env python3
"""Focused provider preview/deployment for ziplio.io app host settings."""
import argparse
import json
import subprocess
import tempfile
from pathlib import Path

SUBSCRIPTION = 'baaf4acd-ff40-43d1-a7ba-6089a2c4f41e'
GROUP = 'rg-schwab-url-shortener'
HOST = 'ziplio.io'
ROUTE_HOST = 'routeshortener.happysand-837faf44.centralus.azurecontainerapps.io'
NAMES = ('ca-shortener-api', 'ca-shortener-web')


def run(args):
    result = subprocess.run(args, text=True, capture_output=True)
    if result.returncode:
        raise RuntimeError(f'{args[0]} failed (exit {result.returncode}); output suppressed')
    return result.stdout


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
    parser.add_argument('--stage', choices=('allowed-hosts', 'public-links'), required=True)
    args = parser.parse_args()
    if json.loads(run(['az', 'account', 'show', '-o', 'json']))['id'] != SUBSCRIPTION:
        raise RuntimeError('default Azure CLI profile is not on the approved subscription')
    if run(['azd', 'env', 'get-value', 'CUSTOM_DOMAIN', '-e', 'prod']).strip().splitlines()[-1] != HOST:
        raise RuntimeError('prod CUSTOM_DOMAIN is not ziplio.io')
    resources = []
    for name in NAMES:
        url = (f'https://management.azure.com/subscriptions/{SUBSCRIPTION}'
               f'/resourceGroups/{GROUP}/providers/Microsoft.App/containerApps/{name}?api-version=2024-03-01')
        live = json.loads(run(['az', 'rest', '--method', 'get', '--url', url, '-o', 'json']))
        p = clean({k: live['properties'][k] for k in
                   ('environmentId', 'configuration', 'template', 'runningStatus', 'workloadProfileName')
                   if k in live['properties']})
        env = p['template']['containers'][0]['env']
        indexed = {item['name']: item for item in env}
        if not {'AllowedHosts', 'PublicLinks__BaseUrl'} <= indexed.keys():
            raise RuntimeError(f'{name} lacks existing host configuration')
        allowed = indexed['AllowedHosts']['value'].split(';')
        if args.stage == 'allowed-hosts':
            for host in (ROUTE_HOST, HOST, 'www.' + HOST):
                if host not in allowed:
                    allowed.append(host)
            indexed['AllowedHosts']['value'] = ';'.join(allowed)
        else:
            if HOST not in allowed or 'www.' + HOST not in allowed:
                raise RuntimeError(f'{name} must allow both custom hosts before changing public links')
            indexed['PublicLinks__BaseUrl']['value'] = 'https://' + HOST + '/'
        resources.append({'type': 'Microsoft.App/containerApps', 'apiVersion': '2024-03-01',
                          'name': name, 'location': live['location'], 'tags': live['tags'],
                          'identity': clean(live['identity']), 'properties': p})
    template = {'$schema': 'https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#',
                'contentVersion': '1.0.0.0', 'resources': resources}
    with tempfile.TemporaryDirectory(prefix='shortener-domain-') as directory:
        path = Path(directory) / 'template.json'
        path.write_text(json.dumps(template))
        command = ['az', 'deployment', 'group', 'what-if' if args.what_if else 'create',
                   '-g', GROUP, '--subscription', SUBSCRIPTION,
                   '--name', 'shortener-domain-' + args.stage + '-20261014', '--template-file', str(path), '-o', 'json']
        if args.what_if:
            command.append('--no-pretty-print')
        result = json.loads(run(command))
    if args.what_if:
        for item in result.get('changes', []):
            if item['changeType'] != 'Ignore':
                print(item['changeType'], item['resourceId'].split('/providers/')[-1],
                      [(d['path'], d['propertyChangeType']) for d in item.get('delta') or []
                       if d['propertyChangeType'] != 'NoEffect'])
    else:
        print('Focused custom-domain ' + args.stage + ' deployment completed.')


if __name__ == '__main__':
    main()
