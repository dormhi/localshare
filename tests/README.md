# Test strategy

Organize tests by component and level (unit, protocol, host integration, Android/device, security). Each test should document prerequisites and whether it can run offline. Follow root `AGENTS.md`: relevant tests and fast static security checks before every commit; broader regression/security checks at phase completion. Never label unavailable Windows/device checks as passed.
