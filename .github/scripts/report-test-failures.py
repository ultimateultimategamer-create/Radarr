#!/usr/bin/env python3
"""Turns failed tests from .trx files into GitHub annotations, readable from the checks API."""
import glob
import os
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def escape(value):
    return value.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def main(folder):
    failures = []
    totals = {"total": 0, "passed": 0, "failed": 0}

    for path in glob.glob(os.path.join(folder, "**", "*.trx"), recursive=True):
        root = ET.parse(path).getroot()

        counters = root.find("t:ResultSummary/t:Counters", NS)
        if counters is not None:
            for key in totals:
                totals[key] += int(counters.get(key, "0"))

        for result in root.iterfind(".//t:UnitTestResult", NS):
            if result.get("outcome") != "Failed":
                continue

            message = result.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS)
            stack = result.findtext("t:Output/t:ErrorInfo/t:StackTrace", default="", namespaces=NS)
            failures.append((result.get("testName"), message.strip(), stack.strip()))

    print(f"Tests: {totals['total']} total, {totals['passed']} passed, {totals['failed']} failed")

    if not failures:
        return

    # One annotation listing every failed test, then the details of the first ones
    names = "\n".join(name for name, _, _ in failures)
    print(f"::error title={len(failures)} failed tests::{escape(names[:60000])}")

    for name, message, stack in failures[:8]:
        details = f"{message[:3000]}\n{stack[:3000]}"
        print(f"::error title={escape(name[:200])}::{escape(details)}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "TestResults")
