"""Computes where every widget of the Second Life build tool ends up, from the XUI files, with the layout rules of llview.cpp
(LLView::applyXUILayout), llfloater.cpp (header stretch) and lltabcontainer.cpp (tab panels). Read only: the downloaded files are data.
Run: python -I slmap.py <viewer-src> <out.json>"""
import io, json, os, re, sys
import xml.etree.ElementTree as ET

SRC, OUT = sys.argv[1], sys.argv[2]
XUI = os.path.join(SRC, 'indra', 'newview', 'skins', 'default', 'xui', 'en')
VPAD, MIN_H = 4, 10                       # llview.cpp
HEADER, BORDER, OVERLAP = 25, 1, 1        # widgets/floater.xml header_height; LLPANEL_BORDER_WIDTH (assumed 1); settings.xml UITabCntrButtonPanelOverlap
DEFAULT_H = {'button': 23, 'combo_box': 23, 'flyout_button': 23, 'menu_button': 23}   # widgets/button.xml, combo_box.xml
NOT_VIEWS = {'string', 'item', 'column', 'combo_item', 'row'}


def lines_of(path):
    """name attribute -> line numbers, to point at the source (nth occurrence in file order)."""
    found = {}
    for n, line in enumerate(io.open(path, encoding='utf-8-sig'), 1):
        for m in re.finditer(r'\bname="([^"]*)"', line):
            found.setdefault(m.group(1), []).append(n)
    return found


def num(e, key):
    v = e.get(key)
    return None if v is None else int(float(v))


def place(e, parent_w, parent_h, last, topleft=True):
    """last = (x, y, w, h) of the previous sibling in top-left coordinates. Returns (x, y, w, h, notes).
    topleft=False is the old layout (origin at the bottom left): only the media panels use it, which declare no layout."""
    notes = []
    left, right, top, bottom, width, height = (num(e, k) for k in ('left', 'right', 'top', 'bottom', 'width', 'height'))
    top_pad, top_delta, bottom_delta, left_pad, left_delta = (num(e, k) for k in ('top_pad', 'top_delta', 'bottom_delta', 'left_pad', 'left_delta'))
    if height is None:
        height = DEFAULT_H.get(e.tag)
        if height is None:
            height = MIN_H if top is None else None
        notes.append('altura padrão')
    fix_x = lambda v: v if v >= 0 else parent_w + v
    fix_y = lambda v: v if v >= 0 else parent_h + v
    if any(v is not None and v < 0 for v in (left, right, top, bottom)):
        notes.append('relativo à borda oposta')
    lx, ly, lw, lh = last
    last_bottom = ly + lh
    if width is None and not (left is not None and right is not None):
        width = lw
        notes.append('largura herdada')
    # vertical: how far the bottom edge moves from the previous one
    relative = True
    if not topleft:
        relative = bottom_delta is not None
        new_bottom = last_bottom - bottom_delta if relative else last_bottom + VPAD + (height or MIN_H)
        if bottom is not None and not relative:
            y = (parent_h - fix_y(bottom)) - height
        elif top is not None:
            y = parent_h - fix_y(top)
        else:
            y = new_bottom - height
        x = fix_x(left) if left is not None and left_delta is None else lx + (left_delta or 0)
        return x, y, width, height, notes + ['leiaute antigo (origem embaixo)']
    if bottom_delta is not None:
        new_bottom = last_bottom + bottom_delta
    elif top_pad is not None:
        new_bottom = last_bottom + top_pad + (height or MIN_H)
    elif top_delta is not None:
        new_bottom = ly + top_delta + (height or MIN_H)
    else:
        relative = False
        new_bottom = last_bottom + VPAD + (height or MIN_H) if (left_delta is None and left_pad is None) else last_bottom
    if top is not None:
        y = fix_y(top)
        if height is None:
            height = (fix_y(bottom) - y) if bottom is not None else lh
    elif bottom is not None and not relative:
        y = fix_y(bottom) - height
    else:
        y = new_bottom - height
    # horizontal
    if left_pad is not None and left is None:
        x = lx + lw + left_pad
    elif left_delta is not None:
        x = lx + left_delta
    elif left is not None:
        x = fix_x(left)
    else:
        x = lx
    if right is not None:
        r = fix_x(right) if right != 0 else parent_w
        if left is not None:
            width = r - x
        elif width is not None and left_delta is None and left_pad is None:
            x = r - width
    return x, y, width, height, notes


def children_of(e):
    return [c for c in list(e) if '.' not in c.tag and c.tag not in NOT_VIEWS]


def strings_of(e):
    return {c.get('name'): (c.text or '').strip() for c in list(e) if c.tag.endswith('.string') or c.tag == 'string'}


def describe(e):
    info = {}
    for k in ('label', 'tool_tip', 'control_name', 'enabled_control', 'image_overlay', 'image_unselected', 'image_selected', 'follows', 'min_val', 'max_val', 'increment', 'decimal_digits', 'initial_value', 'label_width', 'menu_filename'):
        if e.get(k) is not None:
            info[k] = e.get(k).strip()
    text = (e.text or '').strip()
    if text:
        info['text'] = ' '.join(text.split())
    info['visible'] = e.get('visible', 'true') != 'false'
    info['enabled'] = e.get('enabled', 'true') != 'false'
    calls = []
    for a, v in e.attrib.items():
        if a.endswith('.function'):
            calls.append('%s=%s' % (a[:-len('.function')], v))
    for c in list(e):
        if c.tag.endswith('_callback') and c.get('function'):
            calls.append('%s=%s(%s)' % (c.tag.split('.')[-1], c.get('function'), c.get('parameter', '')))
    if calls:
        info['callbacks'] = calls
    items = [(c.get('label') or (c.text or '').strip() or c.get('name'), c.get('value')) for c in list(e) if c.tag.endswith('.item') or c.tag in ('item', 'combo_item')]
    if items:
        info['items'] = [{'label': a, 'value': b} for a, b in items]
    return info


def walk(e, rect, origin, path, tab, source, line_index, seen, out, topleft=True):
    """rect = (x, y, w, h) of e inside its parent; origin = absolute position of e's content inside the window."""
    last = (0, -(rect[3] or 0), rect[2] or 0, rect[3] or 0)      # the "previous widget" of a first child sits just above the parent
    for c in children_of(e):
        name = c.get('name') or c.tag
        loaded, csource, clines, cseen = c, source, line_index, seen
        if c.get('filename'):
            csource = c.get('filename')
            full = os.path.join(XUI, csource)
            loaded = ET.parse(full).getroot()
            for k, v in c.attrib.items():
                loaded.set(k, v)
            clines, cseen = lines_of(full), {}
        x, y, w, h, notes = place(loaded, rect[2] or 0, rect[3] or 0, last, topleft)
        is_tab = e.tag == 'tab_container' and c.tag in ('panel',)
        if is_tab:
            tab_h = num(e, 'tab_height') or 21
            hidden = e.get('hide_tabs') == 'true'
            x, y = BORDER, (0 if hidden else tab_h)           # tab_panel_top = H - 1 - (tab_height - overlap), overlap 1
            w, h = rect[2] - 2 * BORDER, rect[3] - y - BORDER
            notes = ['posição dada pelo contêiner de abas']
        last = (x, y, w or 0, h or 0)
        n = seen.get(name, 0)
        if clines is line_index:
            seen[name] = n + 1
        line = (line_index.get(name) or [None])
        line = line[min(n, len(line) - 1)]
        ident = path + '/' + name
        while any(r['id'] == ident for r in out):
            ident += '~'
        ctab = tab
        if is_tab and tab is None:
            ctab = loaded.get('label') or name
        record = {'id': ident, 'tab': ctab or '(janela)', 'tag': c.tag, 'name': name, 'source': source, 'line': line,
                  'x': x, 'y': y, 'w': w, 'h': h, 'abs_x': origin[0] + x, 'abs_y': origin[1] + y, 'notes': notes}
        record.update(describe(loaded))
        out.append(record)
        walk(loaded, (x, y, w, h), (origin[0] + x, origin[1] + y), ident, ctab, csource, clines if csource != source else line_index, cseen if csource != source else seen, out, topleft if loaded.get('layout') is None else loaded.get('layout') == 'topleft')


def floater(filename, key, extra_tabs=None):
    full = os.path.join(XUI, filename)
    root = ET.parse(full).getroot()
    w, h = num(root, 'width'), num(root, 'height')
    stretch = max(0, HEADER - (num(root, 'legacy_header_height') or 0))
    if extra_tabs:                                     # panels the code adds to the tab container (llfloatermediasettings.cpp)
        box = next(c for c in root.iter('tab_container'))
        for f in extra_tabs:
            ET.SubElement(box, 'panel', {'name': f, 'filename': f, 'layout': 'bottomleft'})   # built by the code from their own files
    out = []
    walk(root, (0, 0, w, h), (0, stretch), key, None, filename, lines_of(full), {}, out)
    return {'key': key, 'file': filename, 'title': root.get('short_title') or root.get('title'), 'width': w, 'height': h + stretch,
            'xml_height': h, 'header_height': HEADER, 'children_offset_y': stretch, 'strings': strings_of(root), 'widgets': out}


windows = [
    floater('floater_tools.xml', 'tools'),
    floater('floater_build_options.xml', 'grid'),
    floater('floater_texture_ctrl.xml', 'texture-picker'),
    floater('floater_color_picker.xml', 'color-picker'),
    floater('floater_media_settings.xml', 'media', ['panel_media_settings_general.xml', 'panel_media_settings_security.xml', 'panel_media_settings_permissions.xml']),
    floater('floater_object_weights.xml', 'weights'),
    floater('floater_bulk_perms.xml', 'bulk-permissions'),
]
io.open(OUT, 'w', encoding='utf-8').write(json.dumps(windows, ensure_ascii=False, indent=1))
for win in windows:
    print(win['key'], win['file'], '%dx%d' % (win['width'], win['height']), 'widgets', len(win['widgets']))
