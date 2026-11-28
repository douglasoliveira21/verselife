"""Writes the layout the game's build tool is drawn from: the positions of docs/design/build-editor-coordinate-map.json, only the windows the game has and only what it needs.
Run from the repository root: python -I unity/tools/sl-build-map/game_layout.py"""
import io, json, os

root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..'))
source = json.load(io.open(os.path.join(root, 'docs', 'design', 'build-editor-coordinate-map.json'), encoding='utf-8'))
windows = []
for w in source['windows']:
    if w['key'] not in ('tools', 'grid', 'texture-picker', 'color-picker', 'media'):
        continue
    widgets = []
    for r in w['widgets']:
        if w['key'] == 'media' and ('panel_media_settings_security' in r['id'] or 'panel_media_settings_permissions' in r['id']):
            continue
        g = r['group']
        item = {'n': r['name'], 't': r['type'], 'x': r['lifeverse']['x'], 'y': r['lifeverse']['y'], 'w': r['lifeverse']['w'] or 0, 'h': r['lifeverse']['h'] or 0}
        if g.startswith('Aba '):
            item['tab'] = g[4:]
        elif g.startswith('Modo '):
            item['mode'] = g[5:]
        elif g == 'Informações da seleção':
            item['info'] = True
        if not r['visibleAtLoad']:
            item['hidden'] = True
        if r.get('labelWidth'):
            item['lw'] = float(r['labelWidth'])
        widgets.append(item)
    windows.append({'key': w['key'], 'w': w['lifeverse']['w'], 'h': w['lifeverse']['h'], 'widgets': widgets})
target = os.path.join(root, 'unity', 'UnityClient', 'Assets', 'LifeVerse', 'UI', 'Resources', 'LifeVerse', 'UI', 'BuildLayout.json')
text = json.dumps({'source': source['source']['tag'], 'scale': source['lifeverse']['scale'], 'windows': windows}, ensure_ascii=False, separators=(',', ':'))
io.open(target, 'w', encoding='utf-8', newline='\n').write(text.replace('},{', '},\n{') + '\n')
print('widgets', sum(len(w['widgets']) for w in windows), 'bytes', os.path.getsize(target))
