"""Fail with paths/rule names only: never print a detected credential value."""
from pathlib import Path
import argparse, hashlib, re, subprocess, sys
ROOT = Path(__file__).resolve().parents[1]
SKIP = {'.git','build','dist','bin','obj','gradle-build','.gradle','signing','backups','__pycache__'}
BLOCK_EXT = {'.apk','.aab','.exe','.dll','.pdb','.dpapi','.pfx','.p12','.jks','.keystore','.pem','.key','.jsonl','.db','.sqlite','.log','.zip'}
BLOCK_NAME = {'google-services.json','local.properties','password.txt','.env'}
RULES = {
 'personal-email': rb'[A-Za-z0-9._%+-]+@(?:gmail|naver|daum|hanmail|outlook|hotmail|yahoo)\.[A-Za-z]{2,}',
 'private-key': rb'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
 'github-token': rb'(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})',
 'google-api-key': rb'AIza[0-9A-Za-z_-]{35}',
 'aws-access-key': rb'AKIA[0-9A-Z]{16}',
 'personal-windows-path': rb'[A-Za-z]:[\\/]+Users[\\/]+(?!Public\b|Default\b|<)[A-Za-z0-9_.-]+',
 'real-conversation-id': rb'\b01[0-9a-f]{6}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b',
 'private-ip-literal': rb'\b(?:192\.168\.\d{1,3}\.\d{1,3}|10\.(?!0\.0\b)\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3})\b',
}
JAR_ALLOW = {'android/gradle/wrapper/gradle-wrapper.jar': '81a82aaea5abcc8ff68b3dfcb58b3c3c429378efd98e7433460610fecd7ae45f'}
def git(*args):
 return subprocess.check_output(['git','-C',str(ROOT),*args],stderr=subprocess.DEVNULL)
def check(path,data,scope):
 problems=[];p=Path(path)
 if p.suffix.lower() in BLOCK_EXT or p.name in BLOCK_NAME or 'service-account' in p.name.lower():problems.append('private-or-generated-file')
 if p.suffix.lower()=='.jar' and hashlib.sha256(data).hexdigest()!=JAR_ALLOW.get(path):problems.append('unreviewed-binary-jar')
 if path.startswith('docs/licenses/') or p.suffix.lower() in {'.jar','.ico'}:return [(scope,path,x) for x in problems]
 for name,pattern in RULES.items():
  if re.search(pattern,data,re.I if name in {'personal-windows-path','real-conversation-id'} else 0):problems.append(name)
 return [(scope,path,x) for x in problems]
def main():
 parser=argparse.ArgumentParser();parser.add_argument('--history',action='store_true');opts=parser.parse_args()
 tracked=False
 try:tracked=Path(git('rev-parse','--show-toplevel').decode().strip()).resolve()==ROOT
 except (OSError,subprocess.CalledProcessError):pass
 if tracked:paths=[p for p in git('ls-files','-z').decode().split('\0') if p]
 else:paths=[p.relative_to(ROOT).as_posix() for p in ROOT.rglob('*') if p.is_file() and not set(p.relative_to(ROOT).parts)&SKIP]
 issues=[]
 for path in paths:
  p=ROOT/path
  if not p.is_file():issues.append(('tree',path,'missing-tracked-file'));continue
  issues+=check(path,p.read_bytes(),'tree')
 commits=0
 if opts.history:
  if not tracked:raise SystemExit('--history requires a Git repository at the source root')
  for commit in git('rev-list','--all').decode().splitlines():
   commits+=1
   issues+=check('__commit__',git('cat-file','commit',commit),'history:'+commit[:8])
   for entry in git('ls-tree','-r','-z',commit).split(b'\0'):
    if not entry:continue
    metadata,path=entry.split(b'\t',1);mode,kind,oid=metadata.split()
    if kind==b'blob':issues+=check(path.decode(),git('cat-file','blob',oid.decode()),'history:'+commit[:8])
 for scope,path,rule in sorted(set(issues)):print(f'FAIL {scope}: {path}: {rule}')
 if issues:return 1
 print(f'PASS publication scan: {len(paths)} files; {commits} commits. Pattern check, not a security audit.');return 0
if __name__=='__main__':sys.exit(main())
