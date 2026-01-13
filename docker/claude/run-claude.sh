#!/bin/bash
docker run -it --rm -v "$(pwd)/../..:/workspace" -v "$HOME/.claude:/root/.claude" claude-code
