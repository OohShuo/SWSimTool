# Compact URDF joint parameter layout

The embedded control and its PropertyManager host previously reserved a fixed
height of 130 regardless of the fields displayed. The host continued to reserve
space even when fewer parameters were visible.

The control now positions existing inputs in compact rows, measures wrapped
labels and preferred textbox heights, and reports its content height to the
PropertyManager host. Revolute/prismatic display four rows; continuous displays
two; fixed/root display none. The host reserves the measured content height plus
a small bottom gap. The original font/dialog-unit conversion underestimated the
host height and caused the following component list to overlap the last rows;
that conversion has been removed. Font and width
changes recalculate row geometry without rebuilding controls or changing values.

Validation on 2026-10-09:

- PASS: Release plugin build and source workspace cleanliness check.
- PASS: Test-UrdfLimitLayout.ps1: four/two/zero rows, wrapped labels,
  width/font changes, height notifications and hidden input retention.
- PASS: Test-UrdfExportRetry.ps1: repeated failed Next, direct correction,
  Previous, joint switching and missing editor diagnostics.
- PASS: git diff --check.
- NOT RUN: real SolidWorks GUI/DPI resizing, CAD export and installation.

This change does not alter joint limits, persistence or export semantics.
