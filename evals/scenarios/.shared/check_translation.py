"""Checks a Markdown translation against its source: same structure, untouched code and links, the
target language in the prose, nothing left out. Usage: check_translation.py SOURCE TRANSLATION ru|de
Prints one line per check and exits with 1 when any fails."""
import re
import sys

source_path, target_path, language = sys.argv[1:4]
source = open(source_path, encoding='utf-8').read()
try:
    target = open(target_path, encoding='utf-8').read()
except OSError as error:
    print(f'FAIL translation missing: {error}')
    sys.exit(1)


def split(text):
    """Fenced code blocks, and the prose around them with inline code and URLs removed."""
    code, prose, block, inside = [], [], [], False
    for line in text.splitlines():
        if line.lstrip().startswith('```'):
            if inside:
                code.append('\n'.join(item.rstrip() for item in block))
                block = []
            inside = not inside
            continue
        (block if inside else prose).append(line)
    return code, prose


def urls(text):
    return set(re.findall(r'\]\(([^)\s]+)', text)) | set(re.findall(r'https?://[^\s)"\'>\]]+', text))


def prose_text(lines):
    text = '\n'.join(line for line in lines if not line.lstrip().startswith(('<', '![', '[![')))
    text = re.sub(r'`[^`]*`', ' ', text)
    text = re.sub(r'\]\([^)]*\)', ']', text)
    return re.sub(r'https?://\S+', ' ', text)


source_code, source_prose = split(source)
target_code, target_prose = split(target)
failures = []


def check(name, passed, detail):
    print(('PASS ' if passed else 'FAIL ') + f'{name}: {detail}')
    if not passed:
        failures.append(name)


levels = lambda lines: [len(m.group(1)) for m in (re.match(r'^(#{1,6})\s', line) for line in lines) if m]
check('headings', levels(source_prose) == levels(target_prose),
      f'{len(levels(target_prose))} of {len(levels(source_prose))}, same levels in the same order')

same = sum(1 for a, b in zip(source_code, target_code) if a == b)
check('code blocks unchanged', len(source_code) == len(target_code) and same >= 0.95 * len(source_code),
      f'{len(target_code)} blocks of {len(source_code)}, {same} identical')

kept = urls(source) & urls(target)
check('links kept', len(kept) >= 0.98 * len(urls(source)), f'{len(kept)} of {len(urls(source))}')

source_letters = len(re.findall(r'[^\W\d_]', prose_text(source_prose)))
target_letters = len(re.findall(r'[^\W\d_]', prose_text(target_prose)))
ratio = target_letters / max(1, source_letters)
check('nothing left out', 0.8 <= ratio <= 2.0, f'prose is {ratio:.2f} of the source')

text = prose_text(target_prose)
if language == 'ru':
    cyrillic = len(re.findall(r'[А-Яа-яЁё]', text))
    latin = len(re.findall(r'[A-Za-z]', text))
    share = cyrillic / max(1, cyrillic + latin)
    check('prose in Russian', share >= 0.5, f'{share:.0%} of letters are Cyrillic')
    untranslated = [line for line in target_prose
                    if len(re.findall(r'[A-Za-z]{4,}', prose_text([line]))) >= 12 and not re.search(r'[А-Яа-яЁё]', line)]
    check('no untranslated paragraphs', len(untranslated) <= 0.02 * max(1, len(target_prose)),
          f'{len(untranslated)} long lines without Cyrillic')
elif language == 'de':
    words = re.findall(r'[A-Za-zÄÖÜäöüß]+', text.lower())
    german = sum(word in {'der', 'die', 'das', 'und', 'ist', 'nicht', 'mit', 'für', 'ein', 'eine', 'zu', 'von',
                          'den', 'im', 'auf', 'wird', 'werden', 'sie', 'es', 'dem', 'des', 'bei', 'oder'} for word in words)
    english = sum(word in {'the', 'and', 'is', 'of', 'to', 'with', 'for', 'that', 'are', 'this', 'which', 'when'}
                  for word in words)
    check('prose in German', german >= 0.08 * len(words) and english <= 0.03 * len(words),
          f'{german / max(1, len(words)):.1%} German and {english / max(1, len(words)):.1%} English function words')
else:
    check('language', False, f'unknown language {language}')

sys.exit(1 if failures else 0)
