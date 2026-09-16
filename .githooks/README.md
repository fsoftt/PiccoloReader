# Git hooks

`pre-commit` blocks commits made directly on `main` — everything in this
project goes through a feature branch and a PR.

Git doesn't wire up hooks from a tracked folder automatically; each
clone/machine needs to point Git at this directory once:

```bash
git config core.hooksPath .githooks
```

(On Windows Git Bash, this also works without any extra `chmod` step.)
