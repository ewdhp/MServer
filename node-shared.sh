#!/bin/sh

# Usage: ./node-shared.sh <input_file> <output_file>
# Reads input from <input_file>, processes it, and writes to <output_file>

INPUT_FILE="$1"
OUTPUT_FILE="$2"

if [ ! -f "$INPUT_FILE" ]; then
  echo "Input file not found: $INPUT_FILE" >&2
  exit 1
fi

# Example: just echo the input data (simulate processing)
cat "$INPUT_FILE" | tee "$OUTPUT_FILE"
