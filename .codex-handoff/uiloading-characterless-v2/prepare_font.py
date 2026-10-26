"""Subset the locally available, OFL-licensed Source Han Sans for Loading only."""
import sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parents[1]/'uihome-integration/python-deps'))
from fontTools.ttLib import TTFont
from fontTools import subset
source=Path('/Users/qian/UnityProject/Game3D001/Game3D001/.codely-cli/extensions/TJGenerators/Packages/cn.tuanjie.ai.generators/Editor/Fonts/SourceHanSansSC-Medium.otf')
chars=''.join(chr(c) for c in range(32,127))+'正在进入花漾世界…加载失败，点击重试中'
f=TTFont(source);o=subset.Options();o.name_IDs=['*'];o.name_legacy=True;o.name_languages=['*']
s=subset.Subsetter(options=o);s.populate(text=chars);s.subset(f)
names={1:'HuayangLoading SC Medium',2:'Regular',3:'HuayangLoading-SC-Medium-20261022',4:'HuayangLoading SC Medium',6:'HuayangLoading-SC-Medium',16:'HuayangLoading SC',17:'Medium'}
for n in f['name'].names:
    if n.nameID in names:n.string=names[n.nameID].encode(n.getEncoding())
cff=f['CFF '].cff;cff.fontNames=['HuayangLoading-SC-Medium'];cff.topDictIndex[0].FullName='HuayangLoading SC Medium';cff.topDictIndex[0].FamilyName='HuayangLoading SC'
assert set(map(ord,chars))<=set(f.getBestCmap())
target=ROOT/'fonts/HuayangLoading-SC-Medium.otf';f.save(target)
(ROOT/'fonts/subset-characters.txt').write_text(chars+'\n')
print('Loading font verified:',target.stat().st_size,'bytes')
