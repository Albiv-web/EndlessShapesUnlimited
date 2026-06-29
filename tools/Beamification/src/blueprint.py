from copy import deepcopy as copy
from pathlib import Path
from typing import ClassVar, Dict, ForwardRef, List

import json

from attr import attrib, attrs

import numpy as np
import numpy.typing as npt


GuidMapValue = (
    int
    | float
    | str
    | List[ForwardRef("GuidMapValue")]
    | Dict[str, ForwardRef("GuidMapValue")]
)
GuidMapDef = Dict[str, GuidMapValue]
GuidMap = Dict[str, GuidMapDef]


@attrs(auto_attribs=True)
class Block:
    """Block on a craft"""

    ROTS_Z: ClassVar[npt.NDArray] = np.array(
        [
            [0, 0, 1],
            [1, 0, 0],
            [0, 0, -1],
            [-1, 0, 0],
            [0, -1, 0],
            [0, -1, 0],
            [0, -1, 0],
            [0, -1, 0],
            [0, 1, 0],
            [0, 1, 0],
            [0, 1, 0],
            [0, 1, 0],
            [0, 0, 1],
            [1, 0, 0],
            [0, 0, -1],
            [-1, 0, 0],
            [0, 0, 1],
            [0, 0, -1],
            [0, 0, 1],
            [0, 0, -1],
            [1, 0, 0],
            [-1, 0, 0],
            [1, 0, 0],
            [-1, 0, 0],
        ]
    )

    ROTS_Y: ClassVar[npt.NDArray] = np.array(
        [
            [0, 1, 0],
            [0, 1, 0],
            [0, 1, 0],
            [0, 1, 0],
            [0, 0, 1],
            [1, 0, 0],
            [0, 0, -1],
            [-1, 0, 0],
            [0, 0, 1],
            [1, 0, 0],
            [0, 0, -1],
            [-1, 0, 0],
            [0, -1, 0],
            [0, -1, 0],
            [0, -1, 0],
            [0, -1, 0],
            [1, 0, 0],
            [1, 0, 0],
            [-1, 0, 0],
            [-1, 0, 0],
            [0, 0, 1],
            [0, 0, 1],
            [0, 0, -1],
            [0, 0, -1],
        ]
    )

    ROTS_X: ClassVar[npt.NDArray] = np.array(
        [
            [1, 0, 0],
            [0, 0, -1],
            [-1, 0, 0],
            [0, 0, 1],
            [1, 0, 0],
            [0, 0, -1],
            [-1, 0, 0],
            [0, 0, 1],
            [-1, 0, 0],
            [0, 0, 1],
            [1, 0, 0],
            [0, 0, -1],
            [-1, 0, 0],
            [0, 0, 1],
            [1, 0, 0],
            [0, 0, -1],
            [0, -1, 0],
            [0, 1, 0],
            [0, 1, 0],
            [0, -1, 0],
            [0, 1, 0],
            [0, -1, 0],
            [0, -1, 0],
            [0, 1, 0],
        ]
    )

    guid_entry: GuidMapDef = attrib(repr=False)

    coord: npt.NDArray
    color: int
    rot: int

    @property
    def rot_x(self):
        return Block.ROTS_X[self.rot]

    @property
    def rot_y(self):
        return Block.ROTS_Y[self.rot]

    @property
    def rot_z(self):
        return Block.ROTS_Z[self.rot]

    @property
    def guid(self) -> str:
        return self.guid_entry["ComponentId"]["Guid"]  # type: ignore


@attrs(auto_attribs=True)
class PhantomBlock:
    """Location on a craft occupied by a Block. Also a proxy for it."""

    coord: npt.NDArray
    parent: Block

    def __getattribute__(self, name):
        if name == "coord":
            return super().__getattribute__("coord")
        elif name == "parent":
            return super().__getattribute__("parent")
        return getattr(self.parent, name)


def quaternion_by_vector(q: npt.NDArray, v: npt.NDArray):
    u = q[:3]
    s = q[3]

    return 2 * (u @ v) * u + (s**2 - u @ u) * v + 2 * s * np.cross(u, v)


def quaternion_by_quaternion(q1, q2):
    b1, c1, d1, a1 = q1
    b2, c2, d2, a2 = q2

    return np.array(
        [
            a1 * b2 + b1 * a2 + c1 * d2 - d1 * c2,
            a1 * c2 - b1 * d2 + c1 * a2 + d1 * b2,
            a1 * d2 + b1 * c2 - c1 * b2 + d1 * a2,
            a1 * a2 - b1 * b2 - c1 * c2 - d1 * d2,
        ]
    )


def get_guid_map(streaming_data_path: Path):
    base_items = []
    for path in streaming_data_path.glob("**/*.item"):
        with open(path) as in_:
            base_items.append(json.load(in_))

    item_dups = []
    for path in streaming_data_path.glob("**/*.itemduplicateandmodify"):
        with open(path) as in_:
            item_dups.append(json.load(in_))

    guid_map = {base["ComponentId"]["Guid"]: base for base in base_items}

    # Inject default data
    default_directions = {
        "Up": False,
        "Down": False,
        "Forwards": False,
        "Back": False,
        "Right": False,
        "Left": False,
    }
    default_reference_to_component = {
        "Reference": {"Name": "", "Guid": "00000000-0000-0000-0000-000000000000"}
    }
    default_data = {
        "Health": 100,
        "Weight": 0.01,
        "ArmourClass": 4,
        "eRadarCrossSection": 30,
        "AttachDirections": copy(default_directions),
        "SupportDirections": copy(default_directions),
        "ActiveBlockLink": copy(default_reference_to_component),
        "MirrorLaterialFlipReplacementReference": copy(default_reference_to_component),
        "MirrorVerticalFlipReplacementReference": copy(default_reference_to_component),
        "MeshReference": copy(default_reference_to_component),
        "MaterialReference": copy(default_reference_to_component),
        "Cost": {
            "Material": 0,
        },
        "ExtraSettings": {
            "WaterTight": True,
            "BlockPathfinding": True,
            "ViewMeshWhenPlacing": True,
            "LocalRotationToForward": False,
            "PlaceableOnFortress": True,
            "PlaceableOnStructure": True,
            "PlaceableOnVehicle": True,
            "PlaceableInPrefab": True,
            "PlaceableOnSubConstructable": 2,
            "AutomaticallyGenerateCollider": True,
            "StructuralComponent": False,
            "EmpSusceptibility": 0,
            "EmpResistivity": 1,
            "EmpDamageFactor": 1,
            "FractionHeatDamagePerMeterPenetration": 0.05,
            "ExplosionOnDeath": 0,
            "Flammability": 0.5,
            "FireResistance": 10,
            "CreateListOfTheseBlocks": True,
            "UseCustomName": False,
            "UseALowLodRender": True,
            "AllowsExhaust": False,
            "AllowsVisibleBandTransmission": False,
            "AllowsIrBandTransmission": False,
            "AllowsRadarBandTransmission": False,
            "AllowsSonarBandTransmission": False,
            "RenderInImportantView": False,
        },
        "DragSettings": {
            "DragClearancePositions": [],
            "DragStopper": True,
            "DragFactorNeg": "1,1,1",
            "DragFactorPos": "1,1,1",
            "Geometry": 0,
        },
        "Code": {
            "GroupConnectionInfo": {
                "SpreadToTypePermissions": "0" * 32,
                "ExtraRightUpForwardElementJumps": "0,0,0",
                "ExtraLeftDownBackElementJumps": "0,0,0",
                "ReceiveDirections": copy(default_directions),
                "SendDirections": copy(default_directions),
                "BlockGroupReference": copy(default_reference_to_component),
            }
        },
        "SizeInfo": {
            "SizePos": {"x": 0, "y": 0, "z": 0},
            "SizeNeg": {"x": 0, "y": 0, "z": 0},
            "VolumeFactor": 1.0,
            "VolumeBuoyancyExtraFactor": 1.0,
            "ArrayPositionsUsed": 1,
            "LocalCenter": "0,0,0",
        },
        "SubObjects": {"SubObjects": []},
        "Sounds": {"Sounds": []},
    }

    for item in guid_map.values():
        sub_dicts = [(default_data, item)]

        while sub_dicts:
            default, target = sub_dicts.pop()

            for key, default_value in default.items():
                if type(default_value) is dict:
                    if key not in target or target[key] is None:
                        target[key] = copy(default_value)
                    else:
                        sub_dicts.append((default_value, target[key]))
                    continue
                if key not in target or target[key] is None:
                    target[key] = default_value

    default_dup = {
        "CostWeightHealthScaling": 1,
        "CostScaling": 1,
        "HealthScaling": 1,
        "ArmourScaling": 1,
        "WeightScaling": 1,
        "VolumeScaling": 1,
        "DisplayOnInventory": True,
        "InventoryTabOrVariantId": copy(default_reference_to_component),
        "MeshReference": copy(default_reference_to_component),
        "MaterialReference": copy(default_reference_to_component),
        "IdToDuplicate": copy(default_reference_to_component),
        "MirrorLaterialFlipReplacementReference": copy(default_reference_to_component),
        "MirrorVerticalFlipReplacementReference": copy(default_reference_to_component),
    }

    for dup_def in item_dups:
        sub_dicts = [(default_dup, dup_def)]

        while sub_dicts:
            default, target = sub_dicts.pop()

            for key, default_value in default.items():
                if type(default_value) is dict:
                    if key not in target or target[key] is None:
                        target[key] = copy(default_value)
                    else:
                        sub_dicts.append((default_value, target[key]))
                    continue
                if key not in target or target[key] is None:
                    target[key] = default_value

    for dup in item_dups:
        guid_target = dup["IdToDuplicate"]["Reference"]["Guid"]
        source_item = copy(guid_map[guid_target])

        source_item["ComponentId"] = dup.get("ComponentId")
        source_item["Description"] = dup.get("Description")
        source_item["ArmourClass"] *= dup["ArmourScaling"]
        source_item["Health"] *= dup["CostWeightHealthScaling"] * dup["HealthScaling"]
        source_item["Weight"] *= dup["CostWeightHealthScaling"] * dup["WeightScaling"]
        source_item["Cost"]["Material"] *= (
            dup["CostWeightHealthScaling"] * dup["CostScaling"]
        )
        source_item["SizeInfo"] = (
            dup["SizeInfo"] if dup["change_SizeInfo"] else source_item["SizeInfo"]
        )
        source_item["DragSettings"] = (
            dup["DragSettings"]
            if dup["change_DragSettings"]
            else source_item["DragSettings"]
        )

        if dup.get("ClassNameOverride"):
            source_item["Code"]["ClassName"] = dup["ClassNameOverride"]

        source_item["MeshReference"] = (
            dup["MeshReference"]
            if dup["MeshReference"]["IsValidReference"]
            else source_item["MeshReference"]
        )
        source_item["MaterialReference"] = (
            dup["MaterialReference"]
            if dup["MaterialReference"]["IsValidReference"]
            else source_item["MaterialReference"]
        )
        source_item["MirrorLaterialFlipReplacementReference"] = (
            dup["MirrorLaterialFlipReplacementReference"]
            if dup["MirrorLaterialFlipReplacementReference"]["IsValidReference"]
            else source_item["MirrorLaterialFlipReplacementReference"]
        )
        source_item["MirrorVerticalFlipReplacementReference"] = (
            dup["MirrorVerticalFlipReplacementReference"]
            if dup["MirrorVerticalFlipReplacementReference"]["IsValidReference"]
            else source_item["MirrorVerticalFlipReplacementReference"]
        )
        source_item["InventoryNameOverride"] = dup.get("InventoryNameOverride")
        source_item["InventoryCategoryNameOverride"] = dup.get(
            "InventoryCategoryNameOverride"
        )
        source_item["DisplayName"] = dup.get("DisplayName")
        source_item["ExtraSettings"]["LocalRotationToForward"] = False

        guid_map[source_item["ComponentId"]["Guid"]] = source_item

    return guid_map


def parse_blueprint(
    bp_path: Path,
    guid_map: GuidMap,
    with_subconstructs=True,
):
    def parser(
        construct,
        pos_offset=np.array([0, 0, 0]),
        local_rotation=np.array([0, 0, 0, 1]),
    ):
        yield from (
            Block(
                guid_entry=guid_map[item_dict[id_]],
                coord=(
                    pos_offset
                    + quaternion_by_vector(
                        local_rotation, np.array([*map(float, coord_string.split(","))])
                    )
                ),
                rot=rotation,
                color=color if color else 0,
            )
            for id_, coord_string, rotation, color in zip(
                construct["BlockIds"],
                construct["BLP"],
                construct["BLR"],
                construct["BCI"],
            )
        )

        # Subconstructs are a pain in the ass because of LocalRotation
        if with_subconstructs:
            for sc in construct["SCs"]:
                if sc["ForceId"] != 0:
                    continue

                sc_pos_offset = pos_offset + [
                    *map(float, sc["LocalPosition"].split(","))
                ]
                sc_local_rotation = quaternion_by_quaternion(
                    local_rotation, [*map(float, sc["LocalRotation"].split(","))]
                )

                yield from parser(sc, sc_pos_offset, sc_local_rotation)

    with bp_path.open("r") as in_:
        bp = json.load(in_)

    item_dict = {int(key): guid for key, guid in bp["ItemDictionary"].items()}
    block_data = [*parser(bp["Blueprint"])]

    # Create a block field
    blocks = []
    for block in block_data:
        size_neg_delta = sum(
            [
                block.rot_x * block.guid_entry["SizeInfo"]["SizeNeg"]["x"],  # type: ignore
                block.rot_y * block.guid_entry["SizeInfo"]["SizeNeg"]["y"],  # type: ignore
                block.rot_z * block.guid_entry["SizeInfo"]["SizeNeg"]["z"],  # type: ignore
            ]
        )
        size_pos_delta = sum(
            [
                block.rot_x * block.guid_entry["SizeInfo"]["SizePos"]["x"],  # type: ignore
                block.rot_y * block.guid_entry["SizeInfo"]["SizePos"]["y"],  # type: ignore
                block.rot_z * block.guid_entry["SizeInfo"]["SizePos"]["z"],  # type: ignore
            ]
        )

        bounds = np.vstack((-size_neg_delta, size_pos_delta))
        bounds_min = np.min(bounds, axis=0)
        bounds_max = np.max(bounds, axis=0)

        for dz in range(bounds_min[2], bounds_max[2] + 1):
            for dy in range(bounds_min[1], bounds_max[1] + 1):
                for dx in range(bounds_min[0], bounds_max[0] + 1):
                    added_block = block
                    if dx != 0 or dy != 0 or dz != 0:
                        added_block = PhantomBlock(
                            coord=block.coord + (dx, dy, dz), parent=block
                        )
                    blocks.append(added_block)

    color_map = color_map = np.array(
        [
            [*map(float, color_string.split(","))]
            for color_string in bp["Blueprint"]["COL"]
        ]
    )

    return bp, blocks, color_map
