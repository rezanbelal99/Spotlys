"""
Parses the *same* `ConnectionStrings__Spotlys` environment variable .NET's
`IConfiguration.GetConnectionString("Spotlys")` reads (an Npgsql-style
`Host=...;Port=...;Database=...;Username=...;Password=...` string), rather than inventing a
second, redundant DATABASE_URL for the Python side.
"""

from __future__ import annotations

import os

import psycopg
from dotenv import load_dotenv


def _parse_npgsql_connection_string(value: str) -> dict[str, str]:
    parsed: dict[str, str] = {}
    for pair in value.split(";"):
        pair = pair.strip()
        if not pair:
            continue
        key, _, val = pair.partition("=")
        parsed[key.strip().lower()] = val.strip()
    return parsed


def get_connection() -> psycopg.Connection:
    """A new connection to Spotlys's Postgres, read from the same env var / .env file the
    .NET services use."""
    load_dotenv()
    raw = os.environ.get("ConnectionStrings__Spotlys")
    if not raw:
        raise RuntimeError(
            "Missing ConnectionStrings__Spotlys -- copy .env.example to .env at the repo root."
        )

    parts = _parse_npgsql_connection_string(raw)
    return psycopg.connect(
        host=parts["host"],
        port=parts.get("port", "5432"),
        dbname=parts["database"],
        user=parts["username"],
        password=parts["password"],
    )
