FROM mcr.microsoft.com/dotnet/sdk:10.0

# Install dependencies
RUN apt-get update && \
    apt-get install -y \
    curl \
    git \
    procps \
    libgcc1 \
    libstdc++6 \
    ripgrep \
    ca-certificates && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Install Claude Code using the native installer
RUN curl -fsSL https://claude.ai/install.sh | bash

# Add Claude to PATH
ENV PATH="/root/.local/bin:${PATH}"

# Set working directory
WORKDIR /workspace

# Run Claude Code on container start
CMD ["claude"]