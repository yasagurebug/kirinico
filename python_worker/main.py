import contextlib
import importlib
import importlib.metadata
import json
import os
import sys
import traceback
from pathlib import Path

os.environ.setdefault("NUMBA_THREADING_LAYER", "workqueue")

_original_metadata_version = importlib.metadata.version


def _safe_metadata_version(name: str) -> str:
    if name.lower() == "pymatting":
        return "0"

    return _original_metadata_version(name)


importlib.metadata.version = _safe_metadata_version

import numpy as np
from PIL import Image
from pymatting import estimate_alpha_cf, estimate_alpha_knn, estimate_alpha_lkm
from pymatting.preconditioner.ichol import ichol
from pymatting.preconditioner.jacobi import jacobi


def load_rgb(path: str) -> np.ndarray:
    image = Image.open(path).convert("RGB")
    return np.asarray(image, dtype=np.float64) / 255.0


def load_trimap(path: str) -> np.ndarray:
    trimap = Image.open(path).convert("L")
    trimap_array = np.asarray(trimap, dtype=np.float64) / 255.0
    trimap_array[trimap_array <= 0.1] = 0.0
    trimap_array[trimap_array >= 0.9] = 1.0
    trimap_array[(trimap_array > 0.1) & (trimap_array < 0.9)] = 0.5
    return trimap_array


def save_alpha(path: str, alpha: np.ndarray) -> None:
    alpha_u8 = np.clip(np.rint(alpha * 255.0), 0, 255).astype(np.uint8)
    Image.fromarray(alpha_u8, mode="L").save(path)


def _normalize_method(value) -> str:
    if isinstance(value, int):
        mapping = {
            0: "cf",
            1: "knn",
            2: "lkm",
        }
        return mapping.get(value, "cf")

    if value is None:
        return "cf"

    return str(value).strip().lower()


def _build_cg_kwargs(method_settings: dict) -> dict:
    kwargs = {
        "maxiter": int(method_settings.get("maxIters", 2000)),
    }

    tolerance = float(method_settings.get("tolerance", 1e-7))
    if tolerance > 0.0:
        kwargs["rtol"] = tolerance

    return kwargs


def _build_preconditioner(method_settings: dict, default_name: str | None):
    preconditioner_name = method_settings.get("preconditioner", default_name)
    if preconditioner_name is None:
        return None

    name = str(preconditioner_name).strip().lower()
    if name in ("", "default"):
        name = default_name

    if name in (None, "", "none"):
        return None

    if name == "jacobi":
        return jacobi

    if name == "ichol":
        discard_threshold = float(method_settings.get("discardThreshold", 1e-5))
        shift = max(0.0, float(method_settings.get("shift", 1e-6)))

        def create_ichol(matrix):
            return ichol(
                matrix,
                discard_threshold=discard_threshold,
                shifts=[shift],
            )

        return create_ichol

    raise ValueError(f"unsupported preconditioner: {preconditioner_name}")


def estimate_alpha(request: dict) -> dict:
    image_path = request["imagePath"]
    trimap_path = request["trimapPath"]
    output_alpha_path = request["outputAlphaPath"]
    settings = request.get("settings", {})
    method = _normalize_method(request.get("method"))
    cf = settings.get("cf", {})
    knn = settings.get("knn", {})
    lkm = settings.get("lkm", {})

    image = load_rgb(image_path)
    trimap = load_trimap(trimap_path)
    # PyMatting may print performance warnings to stdout; redirect them to stderr
    # so the JSONL protocol on stdout stays parseable.
    with contextlib.redirect_stdout(sys.stderr):
        if method == "cf":
            cg_kwargs = _build_cg_kwargs(cf)
            alpha = estimate_alpha_cf(
                image,
                trimap,
                preconditioner=_build_preconditioner(cf, "ichol"),
                laplacian_kwargs={
                    "epsilon": float(cf.get("epsilon", 1e-7)),
                    "radius": int(cf.get("radius", 1)),
                },
                cg_kwargs=cg_kwargs,
            )
        elif method == "knn":
            cg_kwargs = _build_cg_kwargs(knn)
            alpha = estimate_alpha_knn(
                image,
                trimap,
                preconditioner=_build_preconditioner(knn, "jacobi"),
                laplacian_kwargs={
                    "n_neighbors": [
                        int(knn.get("neighbors1", 20)),
                        int(knn.get("neighbors2", 10)),
                    ],
                    "distance_weights": [
                        float(knn.get("distanceWeight1", 2.0)),
                        float(knn.get("distanceWeight2", 0.1)),
                    ],
                    "kernel": str(knn.get("kernel", "binary")),
                },
                cg_kwargs=cg_kwargs,
            )
        elif method == "lkm":
            cg_kwargs = _build_cg_kwargs(lkm)
            alpha = estimate_alpha_lkm(
                image,
                trimap,
                laplacian_kwargs={
                    "epsilon": float(lkm.get("epsilon", 1e-7)),
                    "radius": int(lkm.get("radius", 10)),
                },
                cg_kwargs=cg_kwargs,
            )
        else:
            raise ValueError(f"unsupported matting method: {request.get('method')}")

    Path(output_alpha_path).parent.mkdir(parents=True, exist_ok=True)
    save_alpha(output_alpha_path, alpha)

    return {
        "id": request["id"],
        "ok": True,
        "width": int(alpha.shape[1]),
        "height": int(alpha.shape[0]),
    }


def main() -> int:
    for line in sys.stdin:
        if not line:
            continue

        line = line.lstrip("\ufeff").strip()
        if not line:
            continue

        try:
            request = json.loads(line)
            command = request.get("command")
            if command != "estimate_alpha":
                response = {
                    "id": request.get("id", ""),
                    "ok": False,
                    "error": f"unsupported command: {command}",
                }
            else:
                response = estimate_alpha(request)
        except Exception as exc:
            response = {
                "id": request.get("id", "") if "request" in locals() else "",
                "ok": False,
                "error": f"{exc.__class__.__name__}: {exc}",
            }
            traceback.print_exc(file=sys.stderr)

        sys.stdout.write(json.dumps(response, ensure_ascii=True) + "\n")
        sys.stdout.flush()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
