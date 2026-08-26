from pathlib import Path
import asyncio
import os
import sys

package_path = str(Path(__file__).resolve().parents[1] / ".tools" / "python-packages")
sys.path.insert(0, package_path)
os.environ["PYTHONPATH"] = os.pathsep.join(
    part for part in (package_path, os.environ.get("PYTHONPATH")) if part
)
if sys.platform == "win32":
    asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())

import nbformat
from nbclient import NotebookClient


notebook_path = Path(__file__).resolve().parent / "conveyor_st_hubert_2026-07-12.ipynb"
notebook = nbformat.read(notebook_path, as_version=4)

client = NotebookClient(
    notebook,
    timeout=120,
    kernel_name="python3",
    resources={"metadata": {"path": str(notebook_path.parent)}},
)
client.execute()
nbformat.write(notebook, notebook_path)

errors = [
    output
    for cell in notebook.cells
    if cell.cell_type == "code"
    for output in cell.get("outputs", [])
    if output.get("output_type") == "error"
]
if errors:
    raise RuntimeError(f"Notebook contains {len(errors)} execution error(s).")

executed_cells = sum(
    1
    for cell in notebook.cells
    if cell.cell_type == "code" and cell.get("execution_count") is not None
)
print(f"Executed {executed_cells} code cells with no errors: {notebook_path}")
