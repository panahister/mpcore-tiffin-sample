#!/usr/bin/env python3
"""Writes the message files of one service from one table: a key, and its text in every language.

    scripts/tools/resx.py <messages.json>

The table is a JSON file next to the .resx files it produces:

    { "class": "OrderingMessages", "languages": ["en", "fa", "tr"],
      "messages": { "ordering.order_not_found": ["There is no such order.", "...", "..."] } }

The first language is the default and is written to <class>.resx; every other one to <class>.<language>.resx.
A text names its arguments in braces, the same names in every language.
"""
import json
import os
import re
import sys
from xml.sax.saxutils import escape

HEAD = '''<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
'''


def main(path):
    table = json.load(open(path, encoding="utf-8"))
    folder, name, languages = os.path.dirname(os.path.abspath(path)), table["class"], table["languages"]
    problems = []
    for key, texts in table["messages"].items():
        if len(texts) != len(languages):
            problems.append(f"{key}: {len(texts)} texts for {len(languages)} languages")
            continue
        arguments = [sorted(set(re.findall(r"\{(\w+)\}", text))) for text in texts]
        if any(a != arguments[0] for a in arguments):
            problems.append(f"{key}: the languages do not name the same arguments: {arguments}")
    if problems:
        sys.exit("\n".join(problems))
    for index, language in enumerate(languages):
        body = "".join(
            f'  <data name="{key}" xml:space="preserve">\n    <value>{escape(texts[index])}</value>\n  </data>\n'
            for key, texts in table["messages"].items())
        suffix = "" if index == 0 else "." + language
        with open(os.path.join(folder, f"{name}{suffix}.resx"), "w", encoding="utf-8") as f:
            f.write(HEAD + body + "</root>\n")
    print(f"{name}: {len(table['messages'])} messages in {', '.join(languages)}")


if __name__ == "__main__":
    main(sys.argv[1])
