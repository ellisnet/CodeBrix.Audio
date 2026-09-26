"""Verify the package split against the pre-change desktop package, without installing it.

Usage: python3 verify_packages.py BASELINE_NUPKG FEED VERSION
"""
from pathlib import Path
import sys
import zipfile
import xml.etree.ElementTree as ET

baseline, feed, version = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
namespace = {'n': 'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'}
packages = {}
for name in ['CodeBrix.Audio.MitLicenseForever', 'CodeBrix.Audio.Core.MitLicenseForever', 'CodeBrix.Audio.ModestSynth.MitLicenseForever']:
    packages[name] = zipfile.ZipFile(feed / f'{name}.{version}.nupkg')
core = packages['CodeBrix.Audio.Core.MitLicenseForever']
desktop = packages['CodeBrix.Audio.MitLicenseForever']
assert {'lib/net10.0/CodeBrix.Audio.dll', 'lib/net10.0/CodeBrix.Audio.Engine.dll'} <= set(core.namelist())
assert not any(n.startswith('runtimes/') for n in core.namelist())
assert not any(n.endswith('.dll') and n.startswith('lib/') for n in desktop.namelist())
with zipfile.ZipFile(baseline) as old:
    native = [n for n in old.namelist() if n.startswith('runtimes/')]
    assert set(native) == {n for n in desktop.namelist() if n.startswith('runtimes/')}
    for name in native:
        assert old.read(name) == desktop.read(name), f'Desktop native asset changed: {name}'
for name, package in packages.items():
    spec = ET.fromstring(package.read(name + '.nuspec'))
    dependencies = [d.attrib['id'] for d in spec.iter() if d.tag.endswith('}dependency')]
    expected = [] if name == 'CodeBrix.Audio.Core.MitLicenseForever' else ['CodeBrix.Audio.Core.MitLicenseForever']
    assert dependencies == expected, (name, dependencies)
    print(f'{name}: dependencies {dependencies}')
print(f'PASS: managed assembly ownership and dependency graph; all {len(native)} desktop native/license assets byte-identical.')
