#!/usr/bin/env python3
"""Offline safety checks for owned Web redirect URI reconciliation."""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('configure_entra', Path(__file__).with_name('configure-entra.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

CONFIG = {'localRedirectUris': ['https://localhost:63656/signin-oidc', 'https://localhost:63656/signout-callback-oidc'],
          'callbackPaths': ['/signin-oidc', '/signout-callback-oidc']}
PUBLIC = 'routeshortener.happysand-837faf44.centralus.azurecontainerapps.io'
BASE = CONFIG['localRedirectUris'] + [f'https://{PUBLIC}{path}' for path in CONFIG['callbackPaths']]


class RedirectReconciliationTests(unittest.TestCase):
    def test_current_registration_is_noop(self):
        add, remove, unknown, effective = module.reconcile_redirects(CONFIG, BASE, PUBLIC, '', '')
        self.assertEqual((add, remove, unknown), ([], [], []))
        self.assertEqual(effective, sorted(BASE))

    def test_custom_domain_adds_two_callbacks(self):
        add, remove, unknown, _ = module.reconcile_redirects(CONFIG, BASE, PUBLIC, 'links.example.org', '')
        self.assertEqual(add, ['https://links.example.org/signin-oidc',
                               'https://links.example.org/signout-callback-oidc'])
        self.assertEqual((remove, unknown), ([], []))

    def test_display_redacts_query_and_userinfo(self):
        shown = module.safe_uri("https://user:password@partner.example.org/signin-oidc?token=private#fragment")
        self.assertEqual(shown, "https://partner.example.org/signin-oidc [query or fragment redacted]")

    def test_only_owned_stale_hosts_are_removed(self):
        previous = 'old.example.org'
        stale = [f'https://old.azurecontainerapps.io{path}' for path in CONFIG['callbackPaths']]
        old_custom = [f'https://{previous}{path}' for path in CONFIG['callbackPaths']]
        unknown = ['https://partner.example.org/signin-oidc',
                   'https://old.azurecontainerapps.io/other']
        add, remove, warnings, effective = module.reconcile_redirects(
            CONFIG, BASE + stale + old_custom + unknown, PUBLIC, '', previous)
        self.assertEqual(add, [])
        self.assertEqual(remove, sorted(stale + old_custom))
        self.assertEqual(warnings, sorted(unknown))
        self.assertEqual(effective, sorted(BASE + unknown))


if __name__ == '__main__':
    unittest.main()
