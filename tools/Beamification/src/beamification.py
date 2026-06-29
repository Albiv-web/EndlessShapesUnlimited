from threading import Event
from typing import Literal, Tuple
from tqdm import tqdm

from scipy.cluster.vq import kmeans2
from scipy.ndimage import value_indices
from scipy.optimize import Bounds, LinearConstraint, milp
from scipy.sparse import coo_array

import numpy as np
import numpy.typing as npt

BIAS_TYPES = Literal["random"] | Literal["sided"] | Literal["alternate"]


def beamify_procedure(
    s_field: npt.NDArray,
    coeffs: Tuple[float, float, float, float, float, float, float, float, float, float],
    blob_size_threshold=4000,
    failed_solutions_signal=None,
    bias_type: BIAS_TYPES = "random",
    debeamify=False,
):
    blobs = []

    armor_segments = {*s_field.flat} - {0}
    for armor_segment_id in armor_segments:
        armor_mask = s_field == armor_segment_id
        points = np.argwhere(armor_mask)

        cluster_needed = int(np.ceil(len(points) / blob_size_threshold))
        if debeamify or cluster_needed <= 1:
            blobs.append(points)
            continue

        _, blob_ids = kmeans2(points.astype(float), cluster_needed, check_finite=False)
        blobs_discovered = {}
        for blob_id, point in zip(blob_ids, points):
            if blob_id not in blobs_discovered:
                blobs_discovered[blob_id] = []
            blobs_discovered[blob_id].append(point)

        for blob in blobs_discovered.values():
            blobs.append(np.array(blob))

    blobs = sorted(blobs, key=lambda blob: len(blob))

    counter = 1
    result = np.zeros_like(s_field)
    for blob in tqdm(blobs):
        coords_set = {tuple(coord): i for i, coord in enumerate(blob)}
        size = len(blob)

        configuration_data = []
        configuration_coords = []
        boundaries = []

        # 10 configurations
        # 1m,
        # 2m right, 3m right, 4m right
        # 2m down, 3m down, 4m down
        # 2m forward, 3m forward, 4m forward
        for i, (x, y, z) in enumerate(blob):
            added = []

            # Beam acceptance tests
            boundaries.append(True)
            added.append((i, 10 * i + 0))

            fits_2 = not debeamify and (x + 1, y, z) in coords_set
            fits_3 = not debeamify and fits_2 and (x + 2, y, z) in coords_set
            fits_4 = not debeamify and fits_3 and (x + 3, y, z) in coords_set
            boundaries.append(fits_2)
            if boundaries[-1]:
                added.append((i, 10 * i + 1))
            boundaries.append(fits_3)
            if boundaries[-1]:
                added.append((i, 10 * i + 2))
            boundaries.append(fits_4)
            if boundaries[-1]:
                added.append((i, 10 * i + 3))

            fits_2 = not debeamify and (x, y + 1, z) in coords_set
            fits_3 = not debeamify and fits_2 and (x, y + 2, z) in coords_set
            fits_4 = not debeamify and fits_3 and (x, y + 3, z) in coords_set
            boundaries.append(fits_2)
            if boundaries[-1]:
                added.append((i, 10 * i + 4))
            boundaries.append(fits_3)
            if boundaries[-1]:
                added.append((i, 10 * i + 5))
            boundaries.append(fits_4)
            if boundaries[-1]:
                added.append((i, 10 * i + 6))

            fits_2 = not debeamify and (x, y, z + 1) in coords_set
            fits_3 = not debeamify and fits_2 and (x, y, z + 2) in coords_set
            fits_4 = not debeamify and fits_3 and (x, y, z + 3) in coords_set
            boundaries.append(fits_2)
            if boundaries[-1]:
                added.append((i, 10 * i + 7))
            boundaries.append(fits_3)
            if boundaries[-1]:
                added.append((i, 10 * i + 8))
            boundaries.append(fits_4)
            if boundaries[-1]:
                added.append((i, 10 * i + 9))

            # Cover tests
            if (j := coords_set.get((x - 1, y, z))) is not None:
                added.append((i, 10 * j + 1))
                added.append((i, 10 * j + 2))
                added.append((i, 10 * j + 3))
            if (j := coords_set.get((x, y - 1, z))) is not None:
                added.append((i, 10 * j + 4))
                added.append((i, 10 * j + 5))
                added.append((i, 10 * j + 6))
            if (j := coords_set.get((x, y, z - 1))) is not None:
                added.append((i, 10 * j + 7))
                added.append((i, 10 * j + 8))
                added.append((i, 10 * j + 9))

            if (j := coords_set.get((x - 2, y, z))) is not None:
                added.append((i, 10 * j + 2))
                added.append((i, 10 * j + 3))
            if (j := coords_set.get((x, y - 2, z))) is not None:
                added.append((i, 10 * j + 5))
                added.append((i, 10 * j + 6))
            if (j := coords_set.get((x, y, z - 2))) is not None:
                added.append((i, 10 * j + 8))
                added.append((i, 10 * j + 9))

            if (j := coords_set.get((x - 3, y, z))) is not None:
                added.append((i, 10 * j + 3))
            if (j := coords_set.get((x, y - 3, z))) is not None:
                added.append((i, 10 * j + 6))
            if (j := coords_set.get((x, y, z - 3))) is not None:
                added.append((i, 10 * j + 9))

            configuration_coords.extend(added)
            configuration_data.extend([1] * len(added))

        bounds = Bounds(0, boundaries)  # type: ignore

        constraint = LinearConstraint(
            coo_array(
                (np.array(configuration_data), np.array(configuration_coords).T),
                (size, 10 * size),
            ),
            1,
            1,
        )

        adjusted_coefficients = []

        for x, y, z in blob:
            x_c = x / s_field.shape[0] if bias_type != "random" else 0
            y_c = y / s_field.shape[1] if bias_type != "random" else 0
            z_c = z / s_field.shape[2] if bias_type != "random" else 0

            if bias_type == "alternate":
                if y % 2 == 0 or z % 2 == 0:
                    x_c = 1 - x_c
                if x % 2 == 0 or z % 2 == 0:
                    y_c = 1 - y_c
                if x % 2 == 0 or y % 2 == 0:
                    z_c = 1 - z_c

            adjusted_coefficients.append(coeffs[0])
            # We add a tiny constant bias to
            #   tie-break out-of-grain selections, making them more consistent
            adjusted_coefficients.extend(
                coeff + x_c / 1000 + 1e-4 for coeff in coeffs[1:4]
            )
            adjusted_coefficients.extend(
                coeff + y_c / 1000 + 2e-4 for coeff in coeffs[4:7]
            )
            adjusted_coefficients.extend(
                coeff + z_c / 1000 + 3e-4 for coeff in coeffs[7:10]
            )

        my_coeffs = np.array(adjusted_coefficients)

        solution = milp(
            my_coeffs,
            integrality=1,
            bounds=bounds,
            constraints=constraint,
            options={"presolve": False, "time_limit": 15},
        )

        if not solution.success and failed_solutions_signal is not None:
            failed_solutions_signal.set()
            continue

        for i, decided in enumerate(solution.x):
            if not decided:
                continue

            coord_indx = i // 10
            configuration = i % 10

            x, y, z = blob[coord_indx]

            if configuration == 0:
                result[x, y, z] = counter
            elif configuration == 1:
                result[x : x + 2, y, z] = counter
            elif configuration == 2:
                result[x : x + 3, y, z] = counter
            elif configuration == 3:
                result[x : x + 4, y, z] = counter
            elif configuration == 4:
                result[x, y : y + 2, z] = counter
            elif configuration == 5:
                result[x, y : y + 3, z] = counter
            elif configuration == 6:
                result[x, y : y + 4, z] = counter
            elif configuration == 7:
                result[x, y, z : z + 2] = counter
            elif configuration == 8:
                result[x, y, z : z + 3] = counter
            elif configuration == 9:
                result[x, y, z : z + 4] = counter

            counter += 1

    return result


def get_4m_beams_positions(field):
    result = []
    for xx, yy, zz in value_indices(field, ignore_value=0).values():
        if len(xx) == 4:
            for x, y, z in zip(xx, yy, zz):
                result.append((x, y, z))

    if result:
        return np.vstack(result).T
    return np.array([[], [], []])


def beamify(
    s_field: npt.NDArray, grain_directions="zxy", bias_type: BIAS_TYPES = "random", debeamify=False,
) -> npt.NDArray:
    coeffs = np.array(
        [
            4,  # Single blocks are universally bad
            -2 * 1.1,
            -3 * 1.15,
            -4 * 1.2,
            -2 * 1.1,
            -3 * 1.15,
            -4 * 1.2,
            -2 * 1.1,
            -3 * 1.15,
            -4 * 1.2,
        ]
    )

    if divisor := 2 ** grain_directions.index("x"):
        coeffs[1:4] /= divisor
    if divisor := 2 ** grain_directions.index("y"):
        coeffs[4:7] /= divisor
    if divisor := 2 ** grain_directions.index("z"):
        coeffs[7:10] /= divisor

    s_field = s_field.copy()

    sub_results = []

    current_zone_size = np.count_nonzero(s_field)
    signal = Event()
    while True:
        result = beamify_procedure(
            s_field,
            tuple(coeffs),
            blob_size_threshold=current_zone_size,  # type: ignore
            failed_solutions_signal=signal,
            bias_type=bias_type,
            debeamify=debeamify
        )
        bx, by, bz = get_4m_beams_positions(result)

        sub_results.append(result)

        if signal.is_set():
            current_zone_size //= 2
            signal.clear()

        # Successful run
        if len(bx):
            s_field[bx, by, bz] = 0
        else:
            break

    # Time to gather them together
    final_result = np.zeros_like(s_field)
    counter = 1
    for sub_result in sub_results[:-1]:
        for xx, yy, zz in value_indices(sub_result, ignore_value=0).values():
            if len(xx) == 4:
                final_result[xx, yy, zz] = counter
                counter += 1

    for xx, yy, zz in value_indices(sub_results[-1], ignore_value=0).values():
        final_result[xx, yy, zz] = counter
        counter += 1

    return final_result
