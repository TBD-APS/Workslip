import importlib.util
import os
import sysconfig

stdlib_path = os.path.join(sysconfig.get_paths()['stdlib'], 'pathlib.py')
spec = importlib.util.spec_from_file_location('_workslip_stdlib_pathlib', stdlib_path)
if spec is None or spec.loader is None:
    raise RuntimeError('Unable to load the standard-library pathlib module')
stdlib_pathlib = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stdlib_pathlib)
Path = stdlib_pathlib.Path

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
