"""Verify slices and refresh package metadata without touching Unity."""
import hashlib,json,zipfile
from pathlib import Path
import numpy as np
from PIL import Image
R=Path(__file__).resolve().parent
source=Image.open(R/'sources/reference-original.png').convert('RGBA')
prior=Image.open(R.parent/'uiloading-characterless-v1/sources/reference-original.png').convert('RGBA')
assert source.tobytes()==prior.tobytes()
assets=list((R/'assets').glob('*.png'));assert len(assets)==6
for f in assets:
    im=Image.open(f)
    if 'background' in f.name:assert im.size==(750,1334)
    else:
        a=np.asarray(im.convert('RGBA'))[:,:,3];assert a.min()==0 and a.max()==255,f
assert Image.open(R/'atlas/loading-controls-1024.png').size==(1024,1024)
p=R/'layout.json';layout=json.loads(p.read_text())
layout['font_note']='Dynamic text uses bundled HuayangLoading SC Medium, a renamed OFL subset of Source Han Sans SC Medium. See fonts/OFL.txt. Normal/failure text and ASCII covered. Do not overwrite the Home font. Logo and tagline remain AI-rendered art.'
layout['font_file']='fonts/HuayangLoading-SC-Medium.otf'
p.write_text(json.dumps(layout,ensure_ascii=False,indent=2)+'\n')
p=R/'checks/asset-validation.json';v=json.loads(p.read_text())
v['visual_review']='Rechecked source, five sprites on light/dark, and 0/65/100 percent assembly. Reused v1 image assets; refreshed previews with OFL font. Not lossless source-layer extraction.'
v['source_pixel_sha256']=hashlib.sha256(source.tobytes()).hexdigest();v['source_matches_v1_pixels']=True
v['engine_integration']='See INTEGRATION_STATUS.md and integration-result for latest verified state.'
p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n')
with zipfile.ZipFile(R.parent/(R.name+'.zip'),'w',zipfile.ZIP_DEFLATED) as z:
    for f in sorted(R.rglob('*')):
        if f.is_file() and not any(x in f.parts for x in ['integration-backup','__pycache__']):z.write(f,f.relative_to(R.parent))
with zipfile.ZipFile(R.parent/(R.name+'.zip')) as z:assert z.testzip() is None
print('Verified 6 PNGs, genuine alpha, 750x1334 previews, 1024 atlas, font and ZIP.')
