#!/usr/bin/env python3
"""Narrow Microsoft Graph reconciliation for the SchwabUrlShortener Web app."""
import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import uuid
from pathlib import Path
from urllib.parse import urlencode, urlparse

ROOT = Path(__file__).resolve().parents[1]
GRAPH = 'https://graph.microsoft.com/v1.0'
HOST_RE = re.compile(r'^(?=.{1,253}$)[a-z0-9](?:[a-z0-9.-]*[a-z0-9])$')


def command(args, env=None):
    result = subprocess.run(args, text=True, capture_output=True, env=env)
    if result.returncode:
        raise RuntimeError(f'{args[0]} command failed with exit code {result.returncode}; output suppressed')
    return result.stdout


def azd_value(key):
    value = os.environ.get(key, '').strip()
    if value:
        return value
    try:
        output = command(['azd', 'env', 'get-value', key, '-e', os.environ.get('AZURE_ENV_NAME', 'prod')])
    except RuntimeError:
        return ''
    # azd may print an update notice before the requested value.
    return output.strip().splitlines()[-1].strip() if output.strip() else ''


def hostname(raw, label):
    value = raw.strip().lower().rstrip('.')
    if not value or not HOST_RE.fullmatch(value) or '..' in value or '/' in value:
        raise ValueError(f'{label} must be a DNS hostname without a scheme, path, or port')
    return value


def graph(method, path, body=None):
    # Graph is always scoped to the separate External ID CLI profile. Never
    # switch or overwrite the default Azure-resource profile.
    env = os.environ.copy()
    env['AZURE_CONFIG_DIR'] = env.get('ENTRA_AZURE_CONFIG_DIR', str(Path.home() / '.azure-ziplio'))
    account = json.loads(command(['az', 'account', 'show', '-o', 'json'], env=env))
    if account.get('tenantId') != '6adc36e9-dfe5-469f-8aed-6ef83abcdd19':
        raise RuntimeError('separate Entra CLI profile is not signed into the Ziplio External ID tenant')
    args = ['az', 'rest', '--method', method, '--url', GRAPH + path, '-o', 'json']
    if body is None:
        return json.loads(command(args, env=env))
    fd, filename = tempfile.mkstemp(prefix='shortener-entra-', suffix='.json')
    os.close(fd)
    os.chmod(filename, 0o600)
    try:
        Path(filename).write_text(json.dumps(body), encoding='utf-8')
        args += ['--headers', 'Content-Type=application/json', '--body', '@' + filename]
        output = command(args, env=env)
        return json.loads(output) if output.strip() else {}
    finally:
        Path(filename).unlink(missing_ok=True)


def load_registration(client_id):
    query = urlencode({'$filter': f"appId eq '{client_id}'", '$select': 'id,appId,web'})
    items = graph('get', '/applications?' + query).get('value', [])
    if len(items) != 1 or items[0].get('appId') != client_id:
        raise RuntimeError('expected exactly one matching Web application registration')
    return items[0]


def callback_uri(host, path):
    return 'https://' + host + path


def safe_uri(uri):
    parsed = urlparse(uri)
    try:
        host = parsed.hostname or '[unparseable-host]'
        port = f':{parsed.port}' if parsed.port else ''
    except ValueError:
        host, port = '[unparseable-host]', ''
    shown = f'{parsed.scheme}://{host}{port}{parsed.path}'
    return shown + (' [query or fragment redacted]' if parsed.query or parsed.fragment else '')


def reconcile_redirects(config, current, public_host, custom_domain, previous_custom_domain):
    local = set(config['localRedirectUris'])
    paths = config['callbackPaths']
    if paths != ['/signin-oidc', '/signout-callback-oidc']:
        raise ValueError('unexpected callback paths in declared configuration')
    desired = local | {callback_uri(public_host, path) for path in paths}
    if custom_domain:
        desired |= {callback_uri(custom_domain, path) for path in paths}
    if not all(urlparse(uri).scheme == 'https' for uri in desired):
        raise ValueError('all redirect URIs must use HTTPS')
    current_set = set(current)
    if len(current_set) != len(current):
        raise RuntimeError('registration contains duplicate redirect URIs; review required')
    kept, remove, unknown = set(), set(), set()
    for uri in current_set - desired:
        parsed = urlparse(uri)
        host = (parsed.hostname or '').lower()
        aca_owned = host.endswith('.azurecontainerapps.io') and parsed.path in paths
        previous_owned = bool(previous_custom_domain and host == previous_custom_domain and parsed.path in paths)
        if aca_owned or previous_owned:
            remove.add(uri)
        else:
            kept.add(uri)
            unknown.add(uri)
    add = desired - current_set
    effective = sorted((current_set - remove) | add)
    return sorted(add), sorted(remove), sorted(unknown), effective




def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--dry-run', action='store_true')
    mode.add_argument('--apply', action='store_true')
    parser.add_argument('--simulate-custom-domain', metavar='HOST', help='dry-run only; never changes azd')
    args = parser.parse_args()
    if args.simulate_custom_domain and not args.dry_run:
        parser.error('--simulate-custom-domain requires --dry-run')
    if args.apply and os.environ.get('CI') and os.environ.get('ENTRA_RECONCILE_CI_ENABLED') != 'true':
        print('CI Entra reconciliation skipped: add deployment identity as Web registration owner with Application.ReadWrite.OwnedBy, then set ENTRA_RECONCILE_CI_ENABLED=true.')
        return
    config = json.loads((ROOT / 'entra' / 'web-registration.json').read_text(encoding='utf-8'))
    client_id = config['applicationClientId']
    try:
        if str(uuid.UUID(client_id)) != client_id:
            raise ValueError('invalid declared application client ID')
    except ValueError as error:
        raise ValueError('invalid declared application client ID') from error
    public_host = hostname(azd_value('PUBLIC_HOST'), 'PUBLIC_HOST')
    custom_raw = args.simulate_custom_domain or azd_value('CUSTOM_DOMAIN')
    custom_domain = hostname(custom_raw, 'CUSTOM_DOMAIN') if custom_raw else ''
    previous_raw = azd_value('PREVIOUS_CUSTOM_DOMAIN')
    previous_domain = hostname(previous_raw, 'PREVIOUS_CUSTOM_DOMAIN') if previous_raw else ''
    app = load_registration(client_id)
    print(f'Web registration: {client_id}')
    print(f'Public host: {public_host}')
    if custom_domain:
        print(f'Custom domain: {custom_domain}')
    add, remove, unknown, effective = reconcile_redirects(
        config, app.get('web', {}).get('redirectUris', []), public_host, custom_domain, previous_domain)
    for uri in add: print('ADD ' + safe_uri(uri))
    for uri in remove: print('REMOVE ' + safe_uri(uri))
    for uri in unknown: print('KEEP unrecognized URI: ' + safe_uri(uri))
    if not add and not remove: print('Redirect URIs: no changes')
    if args.dry_run:
        print('Dry run: no Entra writes')
        return
    if add or remove:
        graph('patch', f'/applications/{app["id"]}', {'web': {'redirectUris': effective}})
        verified = load_registration(client_id).get('web', {}).get('redirectUris', [])
        if set(verified) != set(effective):
            raise RuntimeError('Graph PATCH did not produce the previewed redirect URI set')
        print('Redirect URIs: applied and verified')
    if not add and not remove:
        print('Apply: no changes')


if __name__ == '__main__':
    try:
        main()
    except (RuntimeError, ValueError, KeyError, json.JSONDecodeError) as error:
        print('Entra reconciliation failed: ' + str(error), file=sys.stderr)
        raise SystemExit(1)
