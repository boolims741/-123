"""Validate the actual rebuilt Windows load-fix delivery archive."""
import hashlib
import json
from pathlib import Path
import struct
import unittest
import xml.etree.ElementTree as ET
import zipfile

ROOT=Path(__file__).resolve().parents[1]


def pe_info(data):
    offset=struct.unpack_from('<I',data,0x3c)[0]
    if data[:2]!=b'MZ' or data[offset:offset+4]!=b'PE\0\0':
        raise ValueError('Invalid PE image')
    machine=struct.unpack_from('<H',data,offset+4)[0]
    flags=struct.unpack_from('<H',data,offset+22)[0]
    magic=struct.unpack_from('<H',data,offset+24)[0]
    subsystem=struct.unpack_from('<H',data,offset+24+68)[0]
    return machine,flags,magic,subsystem


class WindowsDeliveryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.zip=zipfile.ZipFile(ROOT/'dist/EastAsiaMap_v10_1.zip')
        cls.addClassCleanup(cls.zip.close)
        cls.manifest=json.loads(cls.zip.read('validation.json'))

    def test_archive_crc_paths_and_payload(self):
        self.assertIsNone(self.zip.testzip())
        module=[n for n in self.zip.namelist() if n.startswith('EastAsiaMap_Trial/')]
        self.assertEqual(len(module),9)
        for name in self.zip.namelist():
            self.assertFalse(Path(name).is_absolute())
            self.assertNotIn('..',Path(name).parts)
        self.assertIn('EastAsiaMap_Install.exe',self.zip.namelist())

    def test_installer_manifest_matches_all_actual_payload_bytes(self):
        entries={}
        for line in self.zip.read('payload.sha256').decode().splitlines():
            digest,name=line.split('  ',1)
            self.assertNotIn(name,entries)
            self.assertEqual(hashlib.sha256(self.zip.read(name)).hexdigest(),digest)
            entries[name]=digest
        self.assertEqual(set(entries),{n for n in self.zip.namelist() if n.startswith('EastAsiaMap_Trial/')})

    def test_runtime_and_installer_are_x64_and_installer_is_gui_executable(self):
        runtime=self.zip.read('EastAsiaMap_Trial/bin/Win64_Shipping_Client/EastAsiaMap.Runtime.dll')
        installer=self.zip.read('EastAsiaMap_Install.exe')
        for data in (runtime,installer):
            machine,flags,magic,subsystem=pe_info(data)
            self.assertEqual(machine,0x8664)
            self.assertEqual(magic,0x20b)
        self.assertTrue(pe_info(runtime)[1]&0x2000)
        self.assertFalse(pe_info(installer)[1]&0x2000)
        self.assertEqual(pe_info(installer)[3],2)
        self.assertEqual(runtime,(ROOT/'build/runtime/EastAsiaMap.Runtime.dll').read_bytes())
        self.assertEqual(installer,(ROOT/'build/installer/EastAsiaMap_Install.exe').read_bytes())

    def test_descriptor_and_classic_clr_configuration(self):
        module=ET.fromstring(self.zip.read('EastAsiaMap_Trial/SubModule.xml'))
        self.assertEqual(module.find('Version').get('value'),'v0.10.1.0')
        submodule,=module.findall('./SubModules/SubModule')
        self.assertEqual(submodule.find('DLLName').get('value'),'EastAsiaMap.Runtime.dll')
        self.assertEqual(submodule.find('SubModuleClassType').get('value'),'EastAsiaMap.Runtime.EastAsiaMapSubModule')
        config=ET.fromstring(self.zip.read('EastAsiaMap_Install.exe.config'))
        self.assertEqual(config.find('./startup/supportedRuntime').get('sku'),'.NETFramework,Version=v4.7.2')

    def test_original_game_assets_remain_unchanged(self):
        for name,digest in self.manifest['source_sha256'].items():
            if name.endswith('.bin') or name=='ModuleData/settlements.xml':
                self.assertEqual(hashlib.sha256(self.zip.read('EastAsiaMap_Trial/'+name)).hexdigest(),digest)

    def test_validation_does_not_claim_windows_or_game_execution(self):
        self.assertIs(self.manifest['engine_tested'],False)
        self.assertIs(self.manifest['windows_loader_tested'],False)
        self.assertIs(self.manifest['runtime']['netstandard_dependency'],False)
        self.assertEqual(self.manifest['runtime']['platform'],'AMD64/PE32Plus')
        self.assertIs(self.manifest['installer']['windows_ui_tested'],False)
        self.assertIs(self.manifest['installer']['ntfs_zone_operation_tested'],False)
        self.assertEqual(self.manifest['installer']['source_sha256'],hashlib.sha256((ROOT/'runtime/InstallAndCheck.cs').read_bytes()).hexdigest())


if __name__=='__main__': unittest.main()
