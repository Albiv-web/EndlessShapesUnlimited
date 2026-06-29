import json
from typing import List

from scipy.ndimage import value_indices

import numpy as np
import numpy.typing as npt

from .blueprint import Block, GuidMap
from .s_field import ARMOR_BLOCK_FAMILIES


def make_bp_from_field(
    field: npt.NDArray, guid_map: GuidMap, blocks: List[Block], og_bp
):
    coords_taken = np.array([block.coord for block in blocks])

    x_min, y_min, z_min = map(int, np.min(coords_taken, axis=0))

    # Let's try to construct the vessel
    coord_block_lookup = {tuple(block.coord): block for block in blocks}
    block_family_lookup = {
        parent: [
            child
            for child, maybe_parent in ARMOR_BLOCK_FAMILIES.items()
            if maybe_parent == parent
        ]
        for parent in {*ARMOR_BLOCK_FAMILIES.values()}
    }

    new_blocks = []
    for xx, yy, zz in value_indices(field, ignore_value=0).values():
        size = len(xx)

        blr = 0
        if size > 1:
            is_left_to_right = xx[0] < xx[-1]
            is_up_to_down = yy[0] < yy[-1]
            is_back_to_forward = zz[0] < zz[-1]

            blr = is_left_to_right and 1 or is_up_to_down and 8 or 0

        origin = (x_min + xx[0], y_min + yy[0], z_min + zz[0])
        repl_block = coord_block_lookup[origin]

        parent = ARMOR_BLOCK_FAMILIES[repl_block.guid]
        children = block_family_lookup[parent]

        new_guid = [
            child
            for child in children
            if guid_map[child]["SizeInfo"]["SizePos"]["z"] == size - 1  # type: ignore
        ][0]

        color = repl_block.color

        new_blocks.append((origin, new_guid, blr, color))

    beamified_bp = og_bp.copy()
    removed_indices = set()
    for i, coord in enumerate(beamified_bp["Blueprint"]["BLP"]):
        x, y, z = map(int, coord.split(","))

        if field[x - x_min, y - y_min, z - z_min] > 0:
            removed_indices.add(i)

    item_dict_reverse_lookup = {
        guid: int(num) for num, guid in beamified_bp["ItemDictionary"].items()
    }

    guids_used = {guid for _, guid, _, _ in new_blocks}

    missing_guids = guids_used - {*item_dict_reverse_lookup.keys()}
    used_keys = {*item_dict_reverse_lookup.values()}
    free_id = 1

    for missing_guid in missing_guids:
        while free_id in used_keys:
            free_id += 1
        item_dict_reverse_lookup[missing_guid] = free_id
        used_keys.add(free_id)

    # Time to move affected blocks up so they fall down
    up_shift = int(beamified_bp["Blueprint"]["MaxCords"].split(",")[1]) - int(
        beamified_bp["Blueprint"]["MinCords"].split(",")[1]
    )

    for removed_indx in removed_indices:
        coord_string = beamified_bp["Blueprint"]["BLP"][removed_indx]
        x, y, z = map(int, coord_string.split(","))

        new_coord_string = f"{x},{y+up_shift+10},{z}"

        beamified_bp["Blueprint"]["BLP"][removed_indx] = new_coord_string

    for (x, y, z), guid, rotation, color in new_blocks:
        coord_string = f"{x},{y},{z}"
        item_id = item_dict_reverse_lookup[guid]

        beamified_bp["Blueprint"]["BLP"].append(coord_string)
        beamified_bp["Blueprint"]["BLR"].append(rotation)
        beamified_bp["Blueprint"]["BCI"].append(color)
        beamified_bp["Blueprint"]["BlockIds"].append(item_id)

    new_item_dict = {
        str(item_id): guid for guid, item_id in item_dict_reverse_lookup.items()
    }
    beamified_bp["ItemDictionary"] = new_item_dict

    return json.dumps(beamified_bp)
