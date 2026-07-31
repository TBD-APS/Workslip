from pathlib import Path

transform_path = Path(__file__).with_name('wor-237-transform.py')
content = transform_path.read_text(encoding='utf-8')

old = '''remove_between(
    "src/FE/src/features/superadmin/routes/SuperAdmin.tsx",
    "  if (!canUseSuperadmin) {\\n",
    "  return (\\n",
)
'''
new = '''replace_exact(
    "src/FE/src/features/superadmin/routes/SuperAdmin.tsx",
    """  if (!canUseSuperadmin) {\\n    return (\\n      <DesktopOnlySuperadminScreen\\n        onLogout={() => {\\n          clearOrganizationSession();\\n          logout();\\n        }}\\n      />\\n    );\\n  }\\n\\n""",
    "",
)
'''

if old in content:
    transform_path.write_text(content.replace(old, new, 1), encoding='utf-8')
elif new not in content:
    raise RuntimeError('Expected WOR-237 transform block was not found')
