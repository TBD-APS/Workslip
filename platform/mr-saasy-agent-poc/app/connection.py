from __future__ import annotations

import asyncio
import os

from temporalio.client import Client


async def connect_temporal(max_attempts: int = 60) -> Client:
    address = os.getenv("TEMPORAL_ADDRESS", "localhost:7233")
    namespace = os.getenv("TEMPORAL_NAMESPACE", "default")
    last_error: Exception | None = None

    for _ in range(max_attempts):
        try:
            client = await Client.connect(address, namespace=namespace)
            await client.service_client.check_health()
            return client
        except Exception as error:  # POC bootstrap retry; surfaced after bounded attempts.
            last_error = error
            await asyncio.sleep(0.5)

    raise RuntimeError(f"Temporal was not ready at {address}") from last_error
