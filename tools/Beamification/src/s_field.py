from typing import List
import numpy as np

from .blueprint import Block


ARMOR_BLOCK_FAMILIES = {
    child: parent
    for parent, children in {
        "3cc75979-18ac-46c4-9a5b-25b327d99410": [
            "3cc75979-18ac-46c4-9a5b-25b327d99410",
            "8f9dbf41-6c2d-4e7b-855d-b2432c6942a2",
            "649f2aec-6f59-4157-ac01-0122ce2e6dad",
            "9411e401-27da-4546-b805-3334f200f055",
        ],
        "0c03433e-8947-4e7d-9dec-793526fe06d1": [
            "0c03433e-8947-4e7d-9dec-793526fe06d1",
            "242e07fa-399f-4caa-bfc2-1b77bd2bd538",
            "49714981-369a-4158-aff6-e562ee5f98d5",
            "867cea4e-6ea4-4fe2-a4a1-b6230308f8f1",
        ],
        "e71e6f97-fbe8-4bf5-9645-d15179ba0c17": [
            "e71e6f97-fbe8-4bf5-9645-d15179ba0c17",
            "d5e50322-fbc0-4e09-bfab-050f431146a9",
            "19ee2ba3-9443-4a44-97fd-bad9b1443895",
            "f5d2db25-114e-473a-8313-96831ccd011e",
        ],
        "ab699540-efc8-4592-bc97-204f6a874b3a": [
            "ab699540-efc8-4592-bc97-204f6a874b3a",
            "2a22f176-01c2-42f2-a7d2-2c7054504aa9",
            "46f54639-5f91-4731-93eb-e5c0a7460538",
            "a7f5d8de-4882-4111-9d01-436493e5b2d8",
        ],
        "2f7f61ae-79f1-4139-a790-3f2c26bda4e4": [
            "2f7f61ae-79f1-4139-a790-3f2c26bda4e4",
            "d92c5b73-d0fd-423e-98fc-76b1cd91b524",
            "50bdd099-dd8d-43f8-b43d-dd14c60be096",
            "6e2afb0f-97b6-4017-b14c-158146da6854",
        ],
        "710ee212-563b-42f8-acd1-57515479524d": [
            "710ee212-563b-42f8-acd1-57515479524d",
            "6cd6c6bd-da8b-483f-ace2-fa427a07d91a",
            "d47815a1-9052-4885-8d17-8c9cb3eab72b",
            "c7a19161-b361-4074-8c51-2398a0a70d1b",
        ],
        "9a0ae372-beb4-4009-b14e-36ed0715af73": [
            "9a0ae372-beb4-4009-b14e-36ed0715af73",
            "de36c624-8c78-4b52-8d86-431cec16a306",
            "39553630-8281-40e4-96fb-b01c1f3537e6",
            "05475442-0e52-4e0b-9fbb-2715f0e54f97",
        ],
    }.items()
    for child in children
}

LOOKUP_ORDER = [*{*ARMOR_BLOCK_FAMILIES.values()}]
ARMOR_LOOKUP = {
    block: LOOKUP_ORDER.index(parent) for block, parent in ARMOR_BLOCK_FAMILIES.items()
}

BEAMS_4M = {
    "9411e401-27da-4546-b805-3334f200f055",
    "867cea4e-6ea4-4fe2-a4a1-b6230308f8f1",
    "f5d2db25-114e-473a-8313-96831ccd011e",
    "a7f5d8de-4882-4111-9d01-436493e5b2d8",
    "6e2afb0f-97b6-4017-b14c-158146da6854",
    "c7a19161-b361-4074-8c51-2398a0a70d1b",
    "05475442-0e52-4e0b-9fbb-2715f0e54f97",
}


def construct_s_field(
    blocks: List[Block],
    exclude_4m_beams=False,
    exclude_colors=[],
):
    coords_taken = np.array([block.coord for block in blocks])

    x_min, y_min, z_min = map(int, np.min(coords_taken, axis=0))
    x_max, y_max, z_max = map(int, np.max(coords_taken, axis=0))

    x_len, y_len, z_len = x_max - x_min + 1, y_max - y_min + 1, z_max - z_min + 1

    s_field = np.full((x_len, y_len, z_len), 0)

    for block in blocks:
        x, y, z = map(int, block.coord)
        x -= x_min
        y -= y_min
        z -= z_min

        if block.guid in ARMOR_LOOKUP:
            if exclude_4m_beams and block.guid in BEAMS_4M:
                continue
            if block.color in exclude_colors:
                continue
            s_field[x, y, z] = 32 * ARMOR_LOOKUP[block.guid] + block.color + 1

    return s_field
