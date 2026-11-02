"""Check tracked/staged source before publication; never print matched secrets."""
from pathlib import Path
import re
import subprocess
import sys


def main():
    root = Path(__file__).resolve().parents[1]
    files = subprocess.check_output(['git', 'ls-files', '-z'], cwd=root).decode().split('\0')
    blocked = []
    forbidden = {'Library', 'Temp', 'Logs', 'TestOutput', 'External', 'UserSettings', '.venv', '__pycache__', 'Build'}
    private_suffixes = {'.alavatar', '.alworld', '.alprop', '.alposes', '.almotion', '.unitypackage', '.zip', '.rar', '.7z', '.pem', '.key'}
    secrets = re.compile(rb'(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)')
    user_path = re.compile(rb'[A-Za-z]:[\\/]+Users[\\/]+(?!Public\b|Default\b)[^\s"<>]+', re.I)
    for name in filter(None, files):
        path = Path(name)
        reason = None
        if forbidden.intersection(path.parts) or path.suffix.lower() in private_suffixes:
            reason = 'private/generated artifact'
        elif path.name == '.env' or path.name.startswith('.env.') and path.name != '.env.example':
            reason = 'environment file'
        elif name.endswith('SaneProjectProbe.cs.txt') or name.startswith('Validation/') or name.endswith('FaceInspection.cs'):
            reason = 'local-only diagnostic'
        # Inspect the Git index, not the working copy: this is what will be published.
        data = subprocess.check_output(['git', 'show', ':' + name], cwd=root)
        if len(data) > 95 * 1024 * 1024:
            reason = 'exceeds source size limit'
        if secrets.search(data):
            reason = 'possible credential'
        if path.suffix.lower() in {'.cs', '.py', '.ps1', '.md', '.json', '.yml', '.txt', '.asset'} and user_path.search(data):
            reason = 'local Windows user path'
        if reason:
            blocked.append((name, reason))
    for name, reason in blocked:
        print(f'BLOCKED {name}: {reason}')
    print(f'Publication check: {len(list(filter(None, files)))} files, {len(blocked)} findings.')
    return bool(blocked)


if __name__ == '__main__':
    sys.exit(main())
