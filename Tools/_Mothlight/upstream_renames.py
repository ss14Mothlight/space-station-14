"""
Mothlight: renames we keep on top of upstream (Starlight), applied to upstream text by
rename_merge_driver.py and rename_fix_merge.py so these never show up as merge conflicts.

  - passenger: upstream "Assistant" job -> our "Passenger". Assistant Manager, research/
    technical/etc. assistants, paths and sprite states are left alone.
  - speso: upstream "NTCredit" -> spesos. As a store currency (prices, currencyWhitelist) it
    becomes "Speso"; NT credit cash entities become the SpaceCash they migrate to in
    Resources/migration.yml.

To add another, write a Rule and append it to RULES.
"""

import os
import re
from bisect import bisect_right
from dataclasses import dataclass
from typing import Callable

_REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

# Returns the replacement, or None to leave the match alone.
Replacer = Callable[[re.Match, str], str | None]


@dataclass
class Rule:
    name: str
    applies: Callable[[str], bool]
    pattern: re.Pattern
    replace: Replacer


def _line_of(text: str, pos: int) -> str:
    return text[text.rfind("\n", 0, pos) + 1:pos]


# --- passenger ---------------------------------------------------------------

# "<prefix>assistant" that is some other job / term, not the passenger.
_ASSIST_SKIP_BEFORE = re.compile(
    r"(?:research|technical|tech|digital|data|personal|cook|delivery|handyman|religious|"
    r"shopkeeper|medical|gardener|clerical|janitor|dummy)[ _-]?$"
    r"|/$"                      # path segment: ".../Assistant.xml"
    r"|state:\s*\"?$"           # sprite state names
    r"|stamp-$"
    r"|(?<![A-Za-z])T$",        # TAssistant, a test-only job
    re.IGNORECASE,
)
_ASSIST_SKIP_AFTER_I = re.compile(r"^[ _-]?managers?", re.IGNORECASE)
_ASSIST_SKIP_AFTER = re.compile(r"^SOP")  # guidebook directory name, never renamed
# Text where "assistant" is just a word.
_ASSIST_SKIP_PATHS = re.compile(r"^Resources/(Changelog/|Locale/[^/]+/(accent|_Starlight/paper|paper)/)")


def _passenger(m: re.Match, text: str) -> str | None:
    after = text[m.end():m.end() + 16]
    if (_ASSIST_SKIP_BEFORE.search(_line_of(text, m.start()))
            or _ASSIST_SKIP_AFTER_I.match(after) or _ASSIST_SKIP_AFTER.match(after)):
        return None
    src = m.group(0)
    if src.isupper():
        return "PASSENGER"
    return "Passenger" if src[0].isupper() else "passenger"


# --- speso -------------------------------------------------------------------

def _load_cash_migrations() -> dict[str, str]:
    table = {}
    try:
        with open(os.path.join(_REPO, "Resources", "migration.yml"), encoding="utf-8") as f:
            for line in f:
                m = re.match(r"^(NTCredit\d*):\s*(SpaceCash\d*)\s*(#.*)?$", line)
                if m:
                    table[m.group(1)] = m.group(2)
    except OSError:
        pass
    return table


_CASH = _load_cash_migrations()
_CURRENCY_KEY = re.compile(r"^\s*[A-Za-z]*currenc[A-Za-z]*\s*:", re.IGNORECASE)
_LIST_ITEM = re.compile(r"^\s*-\s*$")


def _is_currency(m: re.Match, text: str) -> bool:
    if re.match(r"\s*:\s*-?\d", text[m.end():m.end() + 16]):
        return True  # "NTCredit: 300", a price
    before = _line_of(text, m.start())
    if _CURRENCY_KEY.match(before):
        return True  # "currency: NTCredit"
    if _LIST_ITEM.match(before):
        # "- NTCredit": currency if the list belongs to a *currenc* key.
        end = text.rfind("\n", 0, m.start())
        while end > 0:
            start = text.rfind("\n", 0, end) + 1
            line = text[start:end]
            if line.strip() and not line.lstrip().startswith("-"):
                return bool(_CURRENCY_KEY.match(line))
            end = start - 1
    return False


def _speso(m: re.Match, text: str) -> str | None:
    if _is_currency(m, text):
        return "Speso" if m.group(0) == "NTCredit" else None
    return _CASH.get(m.group(0))


# --- rules -------------------------------------------------------------------

RULES = [
    Rule("passenger",
         lambda p: not _ASSIST_SKIP_PATHS.match(p),
         re.compile(r"assistant", re.IGNORECASE),
         _passenger),
    Rule("speso",
         # Prototypes only: maps get NTCredit cash migrated on load, and C# needs a human.
         lambda p: p.startswith("Resources/Prototypes/") and p.endswith(".yml"),
         re.compile(r"(?<![A-Za-z0-9_])NTCredit\d*(?![A-Za-z0-9_])"),
         _speso),
]

_MIGRATION_SELF_MAP = re.compile(r"^\s*([A-Za-z0-9_]+)\s*:\s*\1\s*(#.*)?$")


def rename(text: str, path: str, lines: set[int] | None = None) -> tuple[str, dict[str, int]]:
    """
    Applies every rule that applies to `path`. If `lines` is given, only matches on those
    (1-based) line numbers are replaced; the rest of the text still serves as context.
    Returns the new text and how many replacements each rule made.
    """
    counts: dict[str, int] = {}
    for rule in RULES:
        if not rule.applies(path):
            continue
        n = 0
        line_starts = None
        if lines is not None:
            line_starts = [0] + [m.end() for m in re.finditer("\n", text)]

        def repl(m: re.Match) -> str:
            nonlocal n
            if line_starts is not None:
                if bisect_right(line_starts, m.start()) not in lines:
                    return m.group(0)
            new = rule.replace(m, text)
            if new is None or new == m.group(0):
                return m.group(0)
            n += 1
            return new

        text = rule.pattern.sub(repl, text)
        if n:
            counts[rule.name] = n

    # Upstream migrations like "ToyFigurinePassenger: ToyFigurineAssistant" turn into self-maps.
    if counts and os.path.basename(path) == "migration.yml":
        text = "".join(l for l in text.splitlines(keepends=True) if not _MIGRATION_SELF_MAP.match(l))

    return text, counts


def describe(counts: dict[str, int]) -> str:
    return ", ".join(f"{n} {name}" for name, n in counts.items())
