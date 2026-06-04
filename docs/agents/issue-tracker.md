# Issue tracker: GitHub

Issues and PRDs for this repo live as GitHub issues. Use the `gh` CLI for all issue operations when network access and authentication are available.

## Repository

Remote repository:

```text
https://github.com/lililiLJJ/------.git
```

Infer the repo from `git remote -v`; `gh` does this automatically when run inside this clone.

## Conventions

- Create an issue: `gh issue create --title "..." --body "..."`
- Read an issue: `gh issue view <number> --comments`
- List issues: `gh issue list --state open --json number,title,body,labels,comments`
- Comment on an issue: `gh issue comment <number> --body "..."`
- Apply or remove labels: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- Close an issue: `gh issue close <number> --comment "..."`

## Publishing work

When a skill says "publish to the issue tracker", create a GitHub issue.

When a skill says "fetch the relevant ticket", run `gh issue view <number> --comments`.

If `gh` is unavailable, unauthenticated, or network-restricted, summarize the intended issue content for the user and do not silently fall back to an unrelated tracker.
