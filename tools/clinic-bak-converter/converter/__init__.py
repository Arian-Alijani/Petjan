"""Animora legacy Clinic.bak -> clinic import bundle converter.

Stdlib-only by default. Reads a legacy veterinary-clinic SQL Server backup
(MTF-wrapped MDF) and produces a versioned, validated Animora import bundle
(``clinic-import.animora.json``) that the vendor admin uploads per clinic.

See ``../FORMAT.md`` for the bundle contract and ``../README.md`` for usage.
"""

__version__ = "0.1.0"
BUNDLE_FORMAT_VERSION = 2
