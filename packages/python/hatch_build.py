"""Tags the wheel for one platform when it bundles that platform's binary.

scripts/build-dists.sh sets DIAGNYX_WHEEL_PLATFORM (e.g. win_amd64) after
staging the matching diagnyx binary into src/diagnyx/bin/. Without it, the
build produces the default pure py3-none-any wheel with no binary.
"""

import os

from hatchling.builders.hooks.plugin.interface import BuildHookInterface


class CustomBuildHook(BuildHookInterface):
    def initialize(self, version, build_data):
        platform_tag = os.environ.get("DIAGNYX_WHEEL_PLATFORM")
        if platform_tag:
            build_data["pure_python"] = False
            build_data["tag"] = f"py3-none-{platform_tag}"
