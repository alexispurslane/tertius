---
id: ta-a1g7
status: closed
deps: []
links: []
created: 2026-11-12T14:36:02Z
type: task
priority: 2
assignee: Alexis Purslane
---
# GitHub is the primary remote, the knot a push mirror: update the docs and links

On 2026-11-12 Alexis moved the primary home of the repository to GitHub. origin fetches from GitHub and pushes to GitHub, then to the knot. Update every doc that names the knot as the primary, make sure that the pre-push hook works with the new order, and check the GitHub repository's settings.


## Notes

**2026-11-12T14:36:46Z**

Done on 2026-11-12. A test with two local bare repositories (git 2.54.0) showed that git runs the pre-push hook once per push URL and pushes to the second URL after the first fails. After a cleanup commit, the knot gets the commits without it, and the retry sends it to both. GitHub: default branch main, public, description set. No .tangled pipelines exist; .github/workflows/gpu-image.yml is the only CI.
