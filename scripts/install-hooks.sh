#!/bin/sh
# core.hooksPath is local, untracked config: `git clone` does not set it and
# .git/hooks is never cloned. A prevention control that exists in one working
# tree is not a prevention control, so this script is the install path and it
# is named in the README.
set -e
git config core.hooksPath .githooks
echo "hooks installed: core.hooksPath -> .githooks"
echo "verify with: git config --get core.hooksPath"
