#!/usr/bin/env python3
"""Build and run portable driver logic tests; never builds/installs a WDK driver."""
import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compiler", default="clang++")
    parser.add_argument("--sanitize", action="store_true", help="AddressSanitizer and UndefinedBehaviorSanitizer")
    args = parser.parse_args()
    compiler = shutil.which(args.compiler)
    if compiler is None:
        parser.error(f"C++ compiler unavailable: {args.compiler}")
    msvc = Path(compiler).name.lower() in {"cl", "cl.exe"}
    flags = (["/nologo", "/std:c++17", "/EHsc", "/W4", "/WX", "/Zi"] if msvc else
             ["-std=c++17", "-Wall", "-Wextra", "-Wpedantic", "-Werror", "-g"])
    if args.sanitize:
        flags += ["/fsanitize=address"] if msvc else ["-fsanitize=address,undefined", "-fno-omit-frame-pointer"]
    sources = sorted((ROOT / "tests/native").glob("*_test.cpp"))
    if not sources:
        parser.error("no native tests found")
    with tempfile.TemporaryDirectory(prefix="localshare-driver-tests-") as directory:
        for source in sources:
            binary = Path(directory) / (source.stem + (".exe" if msvc else ""))
            output = (["/I" + str(ROOT / "src/windows/driver/core"), str(source),
                       "/Fe:" + str(binary), "/Fo:" + str(Path(directory) / (source.stem + ".obj")),
                       "/Fd:" + str(Path(directory) / (source.stem + ".pdb"))] if msvc else
                      ["-I", str(ROOT / "src/windows/driver/core"), str(source), "-o", str(binary)])
            subprocess.run([compiler, *flags, *output], check=True, timeout=120, cwd=directory)
            subprocess.run([str(binary)], check=True, timeout=30)
    print(f"PASS: {len(sources)} portable C++ test executables; WDK/runtime NOT TESTED")
    if args.sanitize and msvc:
        print("MSVC AddressSanitizer enabled; UndefinedBehaviorSanitizer is unavailable with this compiler")

if __name__ == "__main__":
    main()
