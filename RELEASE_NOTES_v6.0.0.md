# Release Notes — 6.0.0

## Clean rebuild

- Removed the entire legacy WinForms application and all LiteDB dependencies.
- Created a three-project solution containing only Core, WPF, and automated tests.
- Introduced a unique v6 process mutex, data profile, generation marker, installer directory, and registry key.
- Removed legacy startup registry entries that could launch an older application over the new UI.
- Disabled sign-in on a fresh installation and removed all default credentials and the reserved `admin` account.

## Data correctness

- Enforced Saudi identity, mobile, name, city, date, lookup, and length validation in the data layer.
- Preserved immutable sequential file numbers beginning at 1 with a 10,000-patient capacity.
- Forced appointment and task patient snapshots to come from the referenced patient record.
- Applied patient sorting in SQL before the result limit.
- Enforced official workdays, configured closures, working hours, and overlap checks.

## User interface

- WPF on .NET 10 with WPF-UI 4.3 Fluent controls and right-to-left Arabic layout.
- Persistent side navigation, dashboard cards, prominent action buttons, responsive content, empty states, and clickable patient-linked rows.
- Gregorian-only scroll-based date/time entry with numeric and named month display.

## Delivery

- Standalone unpackaged Win32 executable.
- Interactive WiX Toolset 6 Setup.exe with standard Windows Installer UI.
- SHA-256 fingerprints in BUILD-INFO.txt.
- No MSIX/UWP packaging and no silent installation as the user-facing path.
