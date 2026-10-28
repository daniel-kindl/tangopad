#!/usr/bin/env python3
"""Create disposable documents for manual interface checks."""

import argparse
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path("artifacts/ux-samples"))
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    samples = {
        "01-welcome.md": """# A little room to write

Open a file. Find a thought. Keep writing.

This document is for reviewing Tangopad's first interface revision.
Try **bold text**, *emphasis*, and `inline code` in the source and preview.

## A few things to try

- Resize the window and check the file actions.
- Turn Word Wrap and line numbers off and on.
- Select Show Preview, then drag the divider.
- Find “draft” and replace it with “ready”.
- Save a copy, edit it, and try opening another file.

> Your words stay on this device.

## Field notes

| Document | State | Next step |
| --- | --- | --- |
| Travel notes | draft | Add a route |
| Reading list | draft | Add a book |
| Release notes | ready | Review |

```json
{
  "title": "Field notes",
  "offline": true,
  "revision": 1
}
```

## Different voices

Příliš žluťoučký kůň. Café déjà vu. Ελληνικά. 日本語. مرحبًا. 👋 🌿

A long paragraph for Word Wrap: The small editor should leave enough room for the words, keep commands within reach, and make it easy to return to the exact place you were editing. Resize the window to compare the source and preview at different widths. This line should wrap when Word Wrap is enabled and scroll horizontally when it is disabled.

## Search practice

draft Draft DRAFT redraft draft-note
item-001, item-002, item-003

Use the regular expression item-(\\d+) with the replacement entry-$1.
""".encode(),
        "02-search-and-unicode.txt": (
            "Search and replacement practice\n\n"
            "draft Draft DRAFT redraft draft-note\n"
            "item-001, item-002, item-003\n"
            "Repeated word: river river river\n\n"
            "Café / Cafe\u0301 / Příliš žluťoučký kůň / 日本語 / مرحبًا / 👋\n\n"
            + "Long line: " + "The river runs past the old station. " * 24 + "\n"
        ).encode(),
        "03-windows-crlf.txt": b"Windows line endings\r\nSecond line\r\nThird line\r\n",
        "04-mixed-endings.txt": b"CRLF line\r\nLF line\nCR line\rFinal line without an ending",
        "05-preview.textile": """h1. Field notes

A paragraph with *strong text*, _emphasis_, and @inline code@.

h2. Checklist

* Read the source.
* Show the preview.
* Edit this sentence.

bq. Keep the editor simple.

|_. Document|_. State|
|Travel notes|draft|
|Reading list|ready|
""".encode(),
        "06-settings.json": (json.dumps({"title": "Field notes", "offline": True, "revision": 1,
                                        "tags": ["draft", "日本語", "café"]}, ensure_ascii=False, indent=2) + "\n").encode(),
        "07-empty.txt": b"",
        "08-many-lines.txt": "".join(f"Line {i:04d}: A note for scrolling and Go To Line.\n" for i in range(1, 2001)).encode(),
        "09-no-final-newline.txt": "A single line without a final newline. Café 👋".encode(),
    }
    for name, content in samples.items():
        (args.output / name).write_bytes(content)
    print(f"Prepared {len(samples)} documents in {args.output.resolve()}")


if __name__ == "__main__":
    main()
