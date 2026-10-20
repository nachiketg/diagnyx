import datetime
import sys

from diagnyx import DiagnyxLogger

logger = DiagnyxLogger("sdk-test-python")
code = logger.info(
    "python SDK smoke test",
    {"ci": True, "at": datetime.datetime(2026, 1, 2, 3, 4, 5, tzinfo=datetime.timezone.utc)},
)
sys.exit(code)
