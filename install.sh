#!/bin/bash
# Agents Unity Bridge - Quick Installer
#
# Usage:
#   curl -sSL https://raw.githubusercontent.com/WarrenMondeville/agents-unity-bridge/main/install.sh | bash
#
# Or download and run:
#   ./install.sh

set -e

echo "Installing Agents Unity Bridge..."
echo

# Check Python
if ! command -v python3 &> /dev/null; then
    echo "Error: Python 3 is required but not installed."
    exit 1
fi

# Install via pip
echo "Installing pip package..."
python3 -m pip install --user --upgrade agents-unity-bridge

# Get Python user scripts directory
PYTHON_BIN="$(python3 -m site --user-base)/bin"

# Add to PATH if not already there
if ! command -v agents-unity-bridge &> /dev/null; then
    # Determine shell config file
    if [ -n "$ZSH_VERSION" ] || [ "$SHELL" = "/bin/zsh" ]; then
        SHELL_RC="$HOME/.zshrc"
    else
        SHELL_RC="$HOME/.bashrc"
    fi

    # Check if already in rc file
    if [ -f "$SHELL_RC" ] && grep -q "$PYTHON_BIN" "$SHELL_RC" 2>/dev/null; then
        echo "PATH already configured in $SHELL_RC"
    else
        echo "Adding Python scripts to PATH in $SHELL_RC..."
        echo "" >> "$SHELL_RC"
        echo "# Added by Agents Unity Bridge installer" >> "$SHELL_RC"
        echo "export PATH=\"\$PATH:$PYTHON_BIN\"" >> "$SHELL_RC"
    fi

    # Export for current session
    export PATH="$PATH:$PYTHON_BIN"
fi

# Install skill
echo
echo "Installing DeepSeek Harness skill..."
python3 -m agents_unity_bridge.cli install-skill

echo
echo "Installation complete!"
echo
echo "Next steps:"
echo "  1. Add the Unity package to your project:"
echo "     Window > Package Manager > + > Add package from git URL..."
echo "     https://github.com/WarrenMondeville/agents-unity-bridge.git?path=package"
echo
echo "  2. Open DeepSeek Harness in your Unity project directory"
echo
echo "  3. Ask DeepSeek Harness naturally: \"Run the Unity tests\""
echo
