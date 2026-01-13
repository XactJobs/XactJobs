@echo off
docker run -it --rm -v "../../:/workspace" -v "%USERPROFILE%\.claude:/root/.claude" claude-code