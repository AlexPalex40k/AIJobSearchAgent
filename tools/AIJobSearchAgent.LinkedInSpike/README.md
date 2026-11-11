# LinkedIn Job Search Feasibility Spike

This isolated .NET 10 console tool compares a direct HTML request with a visible Selenium-controlled browser. It is a bounded diagnostic, not a production scraper: one public LinkedIn jobs result page and at most ten identifiable listings are inspected per run. It does not use private APIs, SQLite, Ollama, an existing browser profile, or account login.

## Run on Windows

From the repository root, use a public `https://www.linkedin.com/jobs/...` URL:

```powershell
dotnet run --project tools/AIJobSearchAgent.LinkedInSpike -- `
  --mode http `
  --url "https://www.linkedin.com/jobs/search/?keywords=dotnet" `
  --max-jobs 3
```

The Selenium experiment requires an installed Chrome or Chromium browser. Selenium Manager resolves ChromeDriver when possible. The browser is visible by default and uses a fresh WebDriver-managed profile. If Chromium is not in a standard location, provide its executable explicitly:

```powershell
dotnet run --project tools/AIJobSearchAgent.LinkedInSpike -- `
  --mode selenium `
  --url "https://www.linkedin.com/jobs/search/?keywords=dotnet" `
  --max-jobs 10 `
  --browser-binary "C:\path\to\chrome.exe"
```

Visible browser runs maximize the window before LinkedIn navigation. Add `--headless` to use Chrome headless mode with a fixed `1920x1080` viewport.

Add `--json` to write the same small report under the ignored `linkedin-spike-results/` directory. Reports contain URLs and extracted public job data, but no cookies, tokens, profiles, or browser session details.

`--max-jobs` accepts `1` through `10` and defaults to `10`; use a smaller value for a shorter diagnostic run.

## Data and limitations

HTTP mode reports the status code and examines returned HTML plus JSON-LD `JobPosting` metadata. It attempts to identify titles, companies, locations, links, and descriptions. Public search-result HTML commonly exposes only card summaries, so absent descriptions and malformed or missing links are reported rather than inferred.

Selenium mode waits for rendered content, checks whether job cards are visible, and selects up to ten cards in the existing search-results page. It explicitly waits for the selected card to become active and verifies the right-hand details pane by LinkedIn job ID, with title and company as a fallback, before assigning a description. It does not open individual vacancy tabs or intentionally navigate to vacancy pages. The initial search-page navigation is still required, and LinkedIn may make internal network requests when a card is selected.

Descriptions are read only from the details pane's job-description element. Missing panes, failed card selections, stale identity mismatches, empty descriptions, login restrictions, and any unexpected navigation away from the results page are reported while already discovered vacancy metadata is retained. Diagnostic counters show pane descriptions, unavailable descriptions, selection failures, identity mismatches, individual vacancy-page navigations, and total extraction duration.

Selenium reports completed extraction as `Success` or `PartialSuccess`, active access restrictions as `Blocked`, and technical failures as `Failed`; `Loading`, `Ambiguous`, and `NoVacancies` preserve additional diagnostic states. Raw CAPTCHA-related text in scripts or hidden markup is reported separately from a displayed challenge and does not by itself block or downgrade successful extraction. A displayed challenge or verification URL stops further extraction; the tool never attempts to solve or bypass it. A visible login popup is closed when possible, but readable cards are retained even when it remains open.

LinkedIn restricts unauthorized automated scraping. The tool does not bypass login, CAPTCHA, checkpoints, rate limits, or other access controls and stops when it recognizes them. It does not automate login, reuse a private profile, or include anti-detection behavior. Run it only where you have appropriate authorization and record restrictions as results.

No live LinkedIn access is exercised by automated tests, and this repository does not claim a working LinkedIn integration. HTTP is suitable only for further investigation if repeated authorized runs show adequate public metadata. Pane-based Selenium extraction removes up to ten explicit vacancy-page navigations compared with the earlier temporary-tab workflow, but its browser cost, asynchronous and brittle page structure, virtualization, and access restrictions still make it unsuitable as a production provider without separate legal, policy, reliability, and maintenance review.
