"""Fail closed on typical secrets/private paths; report locations, never values."""
import argparse, pathlib, re, subprocess, sys, json
ROOT=pathlib.Path(__file__).resolve().parents[1]
RULES={
 'OpenAI credential':re.compile(r'\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{24,}'),
 'GitHub credential':re.compile(r'\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})'),
 'AWS access key':re.compile(r'\b(?:AKIA|ASIA)[A-Z0-9]{16}\b'),
 'private key':re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),
 'private Windows home':re.compile(r'[A-Za-z]:[\\/]+Users[\\/]+(?!Public\b|Default\b)[^\s"<>]+',re.I),
 'private temporary path':re.compile(r'[A-Za-z]:[\\/]+Temp[\\/]+[^\s"<>]+',re.I),
 'assigned credential':re.compile(r'''(?i)\b(?:api[_-]?key|password|access[_-]?token|client[_-]?secret)\b\s*[:=]\s*["'][A-Za-z0-9_./+\-=]{16,}["']'''),
}
FORBIDDEN={'.env','.git-credentials','id_rsa','id_ed25519'}
SUFFIXES={'.tgstudio','.pfx','.p12','.pem','.key','.log','.pdb','.cookie','.cookies'}
def audit(paths,base):
 errors=[];count=0
 for p in paths:
  if not p.is_file():continue
  rel=p.relative_to(base).as_posix();count+=1
  if p.name in FORBIDDEN or p.suffix.lower() in SUFFIXES or p.name.startswith('.env.'):
   errors.append({'file':rel,'rule':'forbidden local/private file'});continue
  raw=p.read_bytes()
  # Images contain approved pixels, not user metadata. PNG metadata is stripped before publication.
  texts=[raw.decode('utf-8',errors='ignore')]
  if p.suffix.lower() in {'.exe','.dll'}:
   texts += [raw[o:].decode('utf-16le',errors='ignore') for o in (0,1)]
  for name,rule in RULES.items():
   for text in texts:
    hit=rule.search(text)
    if hit:
     errors.append({'file':rel,'line':text.count('\n',0,hit.start())+1,'rule':name});break
 print(json.dumps({'checked_files':count,'findings':errors},ensure_ascii=False))
 return 1 if errors else 0
if __name__=='__main__':
 parser=argparse.ArgumentParser();parser.add_argument('--payload',type=pathlib.Path);args=parser.parse_args()
 if args.payload:
  base=args.payload.resolve();paths=[p for p in base.rglob('*') if p.is_file() and 'engine' not in p.relative_to(base).parts]
 else:
  base=ROOT
  if (base/'.git').exists():
   names=subprocess.check_output(['git','ls-files','-z'],cwd=base).decode().split('\0');paths=[base/n for n in names if n]
  else:paths=[p for p in base.rglob('*') if not any(v in {'.git','engine','dist'} for v in p.relative_to(base).parts) and p.name!='TwitchGrab-Studio.exe']
 sys.exit(audit(paths,base))
