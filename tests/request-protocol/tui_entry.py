import subprocess, sys
with open('CONIN$', 'rb', buffering=0) as inp, open('CONOUT$', 'wb', buffering=0) as out:
    raise SystemExit(subprocess.call(sys.argv[1:], stdin=inp, stdout=out, stderr=out))
