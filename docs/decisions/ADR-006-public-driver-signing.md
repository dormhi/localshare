# ADR-006: Public driver and installer signing route

- Status: proposed; owner decision and Windows validation pending
- Date: 2026-10-15
- Scope: Phase 9–10 release signing; recorded early because it affects CI and packaging

## Context

The product is a public tool that anyone should be able to install from a normal `.exe`, on a stock Windows 11 PC with Secure Boot enabled, without test mode or manual certificate import. Development uses test-signed drivers (ADR-004), which is unacceptable for public users: it needs a boot-configuration change and weakens the user's system.

The LocalShare driver is an IddCx driver, which is a **user-mode UMDF** driver hosted by Microsoft's signed UMDF framework. It contains no kernel-mode binary of its own.

## Facts reviewed (2026-10-15; may change — recheck before release)

- **Kernel-mode code-integrity signing does not apply to UMDF binaries.** A Microsoft engineering blog (archived) states that UMDF drivers do not require Code Integrity signing because they are not kernel-mode code, but the driver package (catalog) still needs a signature to install without the unsigned-driver warning. Source: [Do UMDF drivers require signing?](https://learn.microsoft.com/en-us/archive/blogs/peterwie/do-umdf-drivers-require-signing). This post is old; current Windows 11 behaviour must be confirmed by a clean-machine install test.
- **Catalog signature must chain to a publicly trusted root.** Developer reports show a self-signed catalog is rejected on Windows on ARM with "publisher not established as trusted", and package signing rules do not depend on user vs kernel mode. Sources: [Microsoft Q&A: ARM64 UMDF driver signing](https://learn.microsoft.com/en-us/answers/questions/1458060/arm64-umdf-driver-signing-requirements), [OSR: ARM64 UMDF USB driver signing](https://community.osr.com/t/arm64-umdf-usb-driver-signing/57675).
- **Open-source precedent.** The open-source [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver), also an IddCx/UMDF driver, ships a catalog signed through the free SignPath Foundation programme (GlobalSign code-signing chain) and installs without test mode on x64. Its notes say Windows 11 24H2+ on ARM64 may still need test signing. Source: project README and [issue #458](https://github.com/VirtualDrivers/Virtual-Display-Driver/issues/458).
- **SignPath Foundation eligibility** (free for OSS): OSI license with no proprietary parts; actively maintained and already released; signing team owns the repository; multi-factor authentication; binaries built automatically from the repository in a verifiable CI build; manual approval of every signing request; published code-signing and privacy policies; uninstall and system-change rules for end-user software. Source: [SignPath Foundation terms](https://signpath.org/terms). Apache-2.0 qualifies.
- **Microsoft attestation signing needs an EV certificate.** Hardware Dev Center attestation signing (a Microsoft signature on the package) requires an Extended Validation code-signing certificate registered to the Partner Center account. EV certificates are paid yearly and are usually issued only to a registered organisation, not a private individual. Sources: [Attestation sign Windows drivers](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-attestation), [Register for the hardware program](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/hardware-program-register).
- **Azure Artifact (Trusted) Signing is not a substitute** for driver signing; a Microsoft Q&A answer (April 2026) states it does not sign Windows drivers. Source: [Microsoft Q&A](https://learn.microsoft.com/en-au/answers/questions/5866910/hardware-program-verification-using-azures-trusted).

## Options

| Option | Cost | Public install experience | Risks |
| --- | --- | --- | --- |
| **A. SignPath Foundation OSS certificate** for the driver catalog, host binaries and `.exe` installer | Free | Expected to install on x64 Windows 11 without test mode; installer also gains a trusted publisher name | Must be proven on a clean Secure Boot PC; ARM64 may still be refused; needs GitHub Actions build + WDK in CI, published policies, and an initial public release; every release needs manual approval |
| **B. EV certificate + Microsoft attestation signing** | EV certificate yearly fee; usually requires a company | Microsoft-signed package; the most compatible path, including ARM64 | Cost and organisation requirement; Partner Center registration; attestation-signed drivers are not distributed via Windows Update |
| C. Self-signed or test-signed | Free | Requires test mode or importing a root certificate | Rejected for public use: unsafe and too technical for non-developers |

## Recommendation

1. **Adopt A for v1.0 on Windows 11 x64.** Build the driver, host and installer in GitHub Actions from tagged sources, apply to SignPath Foundation once a first public pre-release exists, and publish the required code-signing and privacy policies (the product already has no telemetry, which keeps the privacy policy simple).
2. **Treat ARM64 as unproven** until a SignPath-signed package installs on a stock Windows 11 ARM64 device. If it fails, ship ARM64 later under B rather than asking users to enable test mode.
3. **Keep B as the upgrade path** if A is refused, if x64 installs fail on stock systems, or when the project gains an organisation that can hold an EV certificate.
4. **Android APK:** sign with a project release key kept outside Git (offline backup plus a CI secret); this needs no paid certificate. Distribution and Android developer-verification rules are decided in Phase 10.

## Validation required before this becomes accepted

- On a clean Windows 11 x64 PC with Secure Boot and Memory Integrity enabled, and without test mode: install the signed package, confirm the virtual monitor appears, then uninstall cleanly.
- Repeat on Windows 11 ARM64 hardware.
- Record results in the Phase 10 report. Until then the project status remains "development builds are test-signed".

## Owner decision

Pending. The owner asked on 2026-10-15 to find a way around the paid route; option A is the proposed answer.
