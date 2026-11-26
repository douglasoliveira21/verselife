import re, json, sys, glob
BS = chr(92)
src_files = sorted(glob.glob(sys.argv[1] + '/SettingsCatalog*.cs'))

def skip_str(s, i):
    j = i; verbatim = False
    while s[j] in '$@':
        verbatim = verbatim or s[j] == '@'; j += 1
    assert s[j] == '"', s[i:i + 20]
    j += 1
    while True:
        c = s[j]
        if c == BS and not verbatim: j += 2; continue
        if c == '"':
            if verbatim and s[j + 1] == '"': j += 2; continue
            return j + 1
        j += 1

def is_str_start(s, j):
    if s[j] == '"': return True
    if s[j] in '$@':
        k = j
        while k < len(s) and s[k] in '$@': k += 1
        return k < len(s) and s[k] == '"'
    return False

def match(s, i):
    depth = 0; j = i
    while j < len(s):
        if is_str_start(s, j): j = skip_str(s, j); continue
        c = s[j]
        if c == "'": j += 4 if s[j + 1] == BS else 3; continue
        if c in '([{': depth += 1
        elif c in ')]}':
            depth -= 1
            if depth == 0: return j
        j += 1
    raise Exception('unbalanced')

def split_top(s):
    parts = []; depth = 0; cur = ''; j = 0
    while j < len(s):
        if is_str_start(s, j):
            k = skip_str(s, j); cur += s[j:k]; j = k; continue
        c = s[j]
        if c in '([{': depth += 1
        elif c in ')]}': depth -= 1
        if c == ',' and depth == 0: parts.append(cur.strip()); cur = ''
        else: cur += c
        j += 1
    if cur.strip(): parts.append(cur.strip())
    return parts

STR = r'"(?:[^"' + BS + BS + r']|' + BS + BS + r'.)*"'

def unescape(body):
    out = ''; j = 0
    while j < len(body):
        c = body[j]
        if c == BS and j + 1 < len(body):
            n = body[j + 1]; out += {'n': '\n', 't': '\t', '"': '"', BS: BS}.get(n, n); j += 2; continue
        out += c; j += 1
    return out

def lit(e):
    e = e.strip()
    if re.fullmatch(STR, e, re.S): return unescape(e[1:-1])
    if e in ('true', 'false'): return e == 'true'
    if re.fullmatch(r'-?\d+', e): return int(e)
    if re.fullmatch(r'-?\d*\.\d+[fdm]?|-?\d+[fd]', e): return float(e.rstrip('fdm'))
    parts = [p.strip() for p in split_plus(e)]
    if len(parts) > 1 and all(re.fullmatch(STR, p, re.S) for p in parts): return ''.join(lit(p) for p in parts)
    return {'expr': e}

def split_plus(e):
    parts = []; cur = ''; j = 0; depth = 0
    while j < len(e):
        if is_str_start(e, j):
            k = skip_str(e, j); cur += e[j:k]; j = k; continue
        c = e[j]
        if c in '([{': depth += 1
        elif c in ')]}': depth -= 1
        if c == '+' and depth == 0: parts.append(cur); cur = ''
        else: cur += c
        j += 1
    parts.append(cur); return parts

def tuples(e):
    return [[lit(a), lit(b)] for a, b in re.findall(r'\(\s*(' + STR + r')\s*,\s*(' + STR + r')\s*\)', e)]

out = []
for f in src_files:
    s = open(f, encoding='utf-8-sig').read()
    for m in re.finditer(r'(?<![A-Za-z_.])(new|Unavail|Action|Info)\(\s*"([a-z][a-z0-9_.]*)"\s*,\s*SettingCategory\.', s):
        ctor = m.group(1); open_i = s.index('(', m.start()); close_i = match(s, open_i)
        args = split_top(s[open_i + 1:close_i]); props = {}
        k = close_i + 1
        while s[k] in ' \t\r\n': k += 1
        if s[k] == '{' and ctor == 'new':
            end = match(s, k)
            for p in split_top(s[k + 1:end]):
                if '=' in p:
                    name, expr = p.split('=', 1); props[name.strip()] = expr.strip()
        d = {'file': f.replace(BS, '/').split('/')[-1]}
        if ctor == 'new':
            d.update(id=lit(args[0]), category=args[1].split('.')[-1], tab=lit(args[2]), label=lit(args[3]), help=lit(args[4]), kind=args[5].split('.')[-1], default=lit(args[6]))
        elif ctor == 'Unavail':
            d.update(id=lit(args[0]), category=args[1].split('.')[-1], tab=lit(args[2]), group=lit(args[3]), column=lit(args[4]), label=lit(args[5]), help=lit(args[6]), kind=args[7].split('.')[-1], default=lit(args[8]), unavailable=lit(args[9]))
        elif ctor == 'Action':
            d.update(id=lit(args[0]), category=args[1].split('.')[-1], tab=lit(args[2]), group=lit(args[3]), column=lit(args[4]), label=lit(args[5]), help=lit(args[6]), kind='Action', default='', button=lit(args[7]), confirm=lit(args[8]) if len(args) > 8 else None)
        else:
            d.update(id=lit(args[0]), category=args[1].split('.')[-1], tab=lit(args[2]), group=lit(args[3]), column=lit(args[4]), label=lit(args[5]), help=lit(args[6]), kind='Info', default='', source=args[7])
        for name, expr in props.items():
            if name in ('Min', 'Max', 'Step', 'Column', 'MaxLength'): d[name.lower()] = lit(expr)
            elif name in ('Unit', 'Group', 'Unavailable', 'ButtonText', 'Confirm', 'RestartRequired', 'PresetControlled'): d[name[0].lower() + name[1:]] = lit(expr)
            elif name == 'Keywords': d['keywords'] = [lit(x) for x in split_top(expr.strip()[1:-1])]
            elif name == 'Choices': t = tuples(expr); d['choices'] = t if t else {'expr': expr}
            elif name == 'ChoiceSource': d['choiceSource'] = expr
            elif name == 'InfoSource': d['source'] = expr
            else: d.setdefault('other', {})[name] = expr
        out.append(d)
json.dump(out, open(sys.argv[2], 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(len(out))
bad = [d['id'] for d in out if any(isinstance(d.get(k), dict) for k in ('default', 'label', 'help', 'unavailable', 'choices', 'min', 'max', 'step'))]
print('unresolved:', len(bad), bad)
