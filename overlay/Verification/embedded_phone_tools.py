"""Read-only evidence and native UI operations for the isolated full Android game package."""
from pathlib import Path
import json, os, re, subprocess, sys, time, xml.etree.ElementTree as ET

ADB='C:/Android/platform-tools/adb.exe'
PACKAGE='local.bnsm.integratedtest'
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'Verification/android-integrated-phone'
OUT.mkdir(exist_ok=True)

def adb(*args):
    serial=['-s',os.environ['BNSM_ANDROID_SERIAL']] if os.environ.get('BNSM_ANDROID_SERIAL') else []
    return subprocess.check_output([ADB,*serial,*args],stderr=subprocess.STDOUT).decode('utf-8',errors='replace')

def nodes():
    focus=adb('shell','dumpsys window | grep mCurrentFocus')
    if not any(package in focus for package in [PACKAGE,'com.google.android.documentsui','com.android.documentsui','com.android.fileexplorer']):
        raise RuntimeError('Phone is outside the game/document picker; UI inspection skipped')
    adb('shell','uiautomator dump /sdcard/bnsm-window.xml')
    return list(ET.fromstring(adb('shell','cat /sdcard/bnsm-window.xml')).iter('node'))

def click(text):
    if text=='控制台':
        b=entry_bounds()
        adb('shell',f'input tap {(b[0]+b[2])//2} {(b[1]+b[3])//2}')
        return
    matching=[n for n in nodes() if n.get('text')==text or n.get('content-desc')==text]
    if not matching: raise RuntimeError('Visible control missing: '+text)
    bounds=list(map(int,re.findall(r'\d+',matching[0].get('bounds'))))
    adb('shell',f'input tap {(bounds[0]+bounds[2])//2} {(bounds[1]+bounds[3])//2}')

def entry_bounds():
    windows=adb('shell','dumpsys window windows')
    for block in re.split(r'\n  Window #\d+',windows):
        if 'PopupWindow' not in block[:100] or f'package={PACKAGE}' not in block:continue
        found=re.search(r'Frames:.*? frame=\[(\d+),(\d+)\]\[(\d+),(\d+)\]',block)
        if found:return list(map(int,found.groups()))
    raise RuntimeError('Game floating window not visible')

def capture(name):
    # Do not capture unrelated apps if the owner uses the phone during verification.
    focused=adb('shell','dumpsys window | grep mCurrentFocus')
    if PACKAGE not in focused:raise RuntimeError('Game is no longer the focused app; screenshot skipped')
    adb('shell','screencap -p /sdcard/bnsm-integrated.png')
    adb('pull','/sdcard/bnsm-integrated.png',str(OUT/(name+'.png')))

def state():
    return json.loads(adb('shell',f'cat /sdcard/Android/data/{PACKAGE}/files/Diagnostics/embedded-state.json'))

if __name__=='__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    if sys.argv[1]=='click':click(sys.argv[2])
    elif sys.argv[1]=='dump':
        for n in nodes():
            text=n.get('text') or n.get('content-desc')
            if text:print(text[:150],n.get('bounds'))
    elif sys.argv[1]=='capture':capture(sys.argv[2])
    elif sys.argv[1]=='state':print(json.dumps(state(),ensure_ascii=False))
