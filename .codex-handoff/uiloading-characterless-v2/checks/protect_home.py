import json,hashlib,sys
from pathlib import Path
P=Path('/Users/qian/UnityProject/Game3D001/Game3D001');R=Path(__file__).parent
targets=['Assets/GameRes/Texture/Home','Assets/GameRes/Jump/Character','Assets/GameRes/Prefabs/UIHomeView.prefab','Assets/GameRes/Prefabs/UIHomeView.prefab.meta','Assets/GameRes/Fonts/HuayangUI-SC-Medium.otf','Assets/Scripts/UI/UIHomeView.cs','Assets/Scripts/UI/Editor/UIHomeViewSetup.cs','Assets/Scenes/main.scene']
files={}
for n in targets:
    p=P/n
    for f in sorted(p.rglob('*')) if p.is_dir() else [p]:
        if f.is_file():files[str(f.relative_to(P))]=hashlib.sha256(f.read_bytes()).hexdigest()
out=R/'protected-project-baseline.json'
if '--check' in sys.argv:
    old=json.loads(out.read_text());changed=[n for n in set(old)|set(files) if old.get(n)!=files.get(n)]
    print(json.dumps({'checked':len(files),'changed':changed}));assert not changed,changed
else:
    assert not out.exists();out.write_text(json.dumps(files,indent=2));print('Recorded protected files:',len(files))
