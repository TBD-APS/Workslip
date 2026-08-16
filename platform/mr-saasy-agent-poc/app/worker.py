from __future__ import annotations

import asyncio
import os

from temporalio.worker import Worker

from .activities import decide_attempt, perform_tool, run_gate
from .connection import connect_temporal
from .workflow import ChangeRunWorkflow


async def main() -> None:
    client = await connect_temporal()
    task_queue = os.getenv("TEMPORAL_TASK_QUEUE", "mr-saasy-agent-poc")
    worker = Worker(
        client,
        task_queue=task_queue,
        workflows=[ChangeRunWorkflow],
        activities=[decide_attempt, perform_tool, run_gate],
    )
    await worker.run()


if __name__ == "__main__":
    asyncio.run(main())
