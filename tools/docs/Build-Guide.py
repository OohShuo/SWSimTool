"""Build a standalone offline guide from the reviewed Markdown and real screenshots."""
from pathlib import Path
import base64
import html
import re
import argparse

ROOT = next(p for p in Path(__file__).resolve().parents if (p / 'SWSimTool.sln').is_file())
parser = argparse.ArgumentParser()
parser.add_argument('--version', default='3.2.1')
args = parser.parse_args()
if not re.fullmatch(r'\d+\.\d+(?:\.\d+)?', args.version):
    parser.error('Version must be major.minor or major.minor.patch')
source = ROOT / f'docs/SWSimTool_{args.version}_使用指南.md'
text = source.read_text(encoding='utf-8')
images = []

def inline(value):
    value = html.escape(value)
    value = re.sub(r'`([^`]+)`', r'<code>\1</code>', value)
    return re.sub(r'\*\*([^*]+)\*\*', r'<strong>\1</strong>', value)

parts, contents = [], []
heading_count = 0
lines = text.splitlines()
i = 0
while i < len(lines):
    line = lines[i]
    if not line.strip():
        i += 1
        continue
    if line.startswith('```'):
        block = []
        i += 1
        while i < len(lines) and not lines[i].startswith('```'):
            block.append(lines[i]); i += 1
        parts.append('<pre>' + html.escape('\n'.join(block)) + '</pre>')
    elif line.startswith('#'):
        level = len(line) - len(line.lstrip('#'))
        title = line[level:].strip()
        anchor = 'section-' + str(heading_count)
        heading_count += 1
        parts.append(f'<h{level} id="{anchor}">{inline(title)}</h{level}>')
        if level == 2:
            contents.append((anchor, title))
    elif line.startswith('|'):
        rows = []
        while i < len(lines) and lines[i].startswith('|'):
            row = [x.strip() for x in lines[i].strip('|').split('|')]
            if not all(re.fullmatch(r':?-+:?', x) for x in row):
                rows.append(row)
            i += 1
        table = '<table><thead><tr>' + ''.join('<th>'+inline(x)+'</th>' for x in rows[0]) + '</tr></thead><tbody>'
        table += ''.join('<tr>'+''.join('<td>'+inline(x)+'</td>' for x in row)+'</tr>' for row in rows[1:])
        parts.append(table + '</tbody></table>')
        continue
    elif line.startswith('!['):
        match = re.fullmatch(r'!\[([^]]*)\]\(([^)]+)\)', line)
        assert match, line
        label, relative = match.groups()
        path = source.parent / relative
        assert path.is_file(), path
        images.append(relative)
        payload = base64.b64encode(path.read_bytes()).decode('ascii')
        parts.append(f'<figure><img src="data:image/jpeg;base64,{payload}" alt="{html.escape(label)}"><figcaption>{inline(label)}</figcaption></figure>')
    else:
        parts.append('<p>' + inline(line) + '</p>')
    i += 1

toc = '<nav aria-label="目录"><h2>目录</h2><ol>' + ''.join(f'<li><a href="#{a}">{inline(t)}</a></li>' for a,t in contents) + '</ol></nav>'
css = '''body{font:17px/1.8 "Microsoft YaHei","Noto Sans CJK SC",sans-serif;color:#222;margin:0;background:#fafafa}
main{max-width:1000px;margin:auto;padding:44px 48px;background:white}h1,h2,h3{color:#000;line-height:1.4}h1{font-size:32px}h2{font-size:25px;margin-top:48px}h3{font-size:20px;margin-top:28px}
p{margin:16px 0}a{color:#185b85}code{font-family:Consolas,monospace;font-size:.92em}pre{padding:18px;background:#f5f5f5;overflow:auto;line-height:1.6}
table{border-collapse:collapse;width:100%;margin:22px 0}th,td{border:1px solid #d9d9d9;padding:12px 15px;text-align:left;vertical-align:middle}th{background:#eaeaea;color:#000}tbody tr:nth-child(even){background:#fafafa}
figure{margin:30px 0}img{display:block;width:100%;height:auto}figcaption{font-size:14px;color:#555;margin-top:8px}nav ol{columns:2;padding-left:24px}nav li{break-inside:avoid}nav{margin:32px 0}
@media(max-width:700px){main{padding:22px}body{font-size:16px}nav ol{columns:1}table{font-size:14px}th,td{padding:8px}}
@media print{body{background:white;font-size:11pt}main{max-width:none;padding:0}h2,h3{break-after:avoid}figure,table{break-inside:avoid}img{max-height:220mm;object-fit:contain}a{color:inherit}@page{size:A4;margin:18mm}}'''
document = '<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>SWSimTool '+args.version+' 使用指南</title><style>'+css+'</style><main>'
document += parts[0] + parts[1] + toc + ''.join(parts[2:]) + '</main></html>'
output = ROOT / 'build/docs' / source.with_suffix('.html').name
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(document, encoding='utf-8')
print(f'{output}: {len(contents)} sections, {len(images)} real screenshots, {output.stat().st_size} bytes')
