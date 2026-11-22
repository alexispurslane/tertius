#!/usr/bin/env bash
# pics.sh <folder>: the two crop sheets of pics/ from the run folders, through sheet.py of the t0-mzhr folder.
M=/private/tmp/t0-y46f/m.sh
"$M" sheet.py "$1/rect-lamps-wall-no-jitter.png" rect-lamps 0,120,560,720 1 "A, long mean=n-A:reference" \
  "B, long mean=n-B:reference" "C, shown=n-C" "D, shown=n-D" "C corrected, shown=n-Cw"
"$M" sheet.py "$1/rect-lamps-wall-long-means.png" rect-lamps 0,120,560,720 1 "A, long mean=n-A:reference" \
  "C, long mean=n-C:reference" "D, long mean=n-D:reference" "C corrected, long mean=n-Cw:reference" \
  "cap 4 clamps off, long mean=n-cap4-nc:reference"
