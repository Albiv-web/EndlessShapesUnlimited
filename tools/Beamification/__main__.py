from argparse import ArgumentParser, ArgumentTypeError, FileType
from itertools import permutations
from pathlib import Path

from tkinter.messagebox import askyesno, showerror
from tkinter.filedialog import askdirectory, askopenfilename, asksaveasfilename
from tkinter.simpledialog import askstring

from src.blueprint import get_guid_map, parse_blueprint
from src.beamification import beamify
from src.s_field import construct_s_field
from src.make_result import make_bp_from_field


def ftd_dir(str_path: str):
    try:
        ftd_dir = Path(str_path)
    except Exception as e:
        raise ArgumentTypeError("Invalid path") from e

    if not ftd_dir.exists():
        raise ArgumentTypeError("Dir does not exist")
    if not ftd_dir.is_dir():
        raise ArgumentTypeError("Not a directory")
    ftd_dir = ftd_dir.joinpath("From_The_Depths_Data/StreamingAssets/")
    if not ftd_dir.exists() or not ftd_dir.is_dir():
        raise ArgumentTypeError("This does not seem to be an FtD dir")

    return ftd_dir


def color_string(string):
    if not string:
        return set()
    else:
        try:
            return {*map(int, map(str.strip, string.split(",")))}
        except:
            raise ArgumentTypeError("Invalid color string")


if __name__ == "__main__":
    main_parser = ArgumentParser("Beamify script")
    subs = main_parser.add_subparsers(title="Modes", dest="mode")

    cli_parser = subs.add_parser("cli")
    gui = subs.add_parser("gui")

    ftd_arg = cli_parser.add_argument(
        "--ftd", help="Path to FtD folder", required=True, type=ftd_dir
    )
    cli_parser.add_argument(
        "--input",
        help="Path to BP to beamify (or - for stdin)",
        required=True,
        type=FileType("r"),
    )
    cli_parser.add_argument(
        "--output",
        help="Where to save beamified BP to (or - for stdout)",
        default="-",
        type=FileType("w"),
    )
    cli_parser.add_argument(
        "--exclude-colors",
        default="",
        type=color_string,
        help="Comma-separated string of colors of blocks we won't touch",
    )

    cli_subparsers = cli_parser.add_subparsers(title="Procedures", dest="procedure")
    cli_parser_beamify = cli_subparsers.add_parser(
        "beamify",
        help="Convert, to the best of its ability, armor blocks into 4m beams to maximize effective HP",
    )
    cli_parser_beamify.add_argument(
        "--grain",
        help="Direction priorities. Should be something like xyz with least important axis being first and most important axis being last",
        choices=["".join(grain) for grain in permutations("xyz", 3)],
        required=True,
    )
    cli_parser_beamify.add_argument(
        "--bias",
        choices=["sided", "alternate", "random"],
        default="random",
        help="If `sided`, will make sure to place beams of different lengths consistently, creating a more unified look. "
        "However, this might create slight HP weakness in armor on left, back and under sides. "
        "`alternate` is like sides, but sides alternate, averaging out HP on all sides. "
        "`random` will place beam haphazardly with no order.",
    )
    cli_parser_beamify.add_argument(
        "--exclude-4m-beams",
        action="store_true",
        help="If specified, we'll not touch beams that are already 4m long.",
    )
    cli_parser_debeamify = cli_subparsers.add_parser(
        "debeamify", help="Convert all eligible armor blocks into 1m variants"
    )

    args = main_parser.parse_args()
    if args.mode == "cli":
        ftd = args.ftd
        bp_path = Path(args.input.name)
        output = args.output
        excluded_colors = args.exclude_colors

        debeamify = args.procedure == "debeamify"

        if args.procedure == "beamify":
            grain = args.grain
            bias = args.bias
            do_exclude_4m = args.exclude_4m_beams
        elif args.procedure == "debeamify":
            grain = "xyz"
            bias = "random"
            do_exclude_4m = False
        else:
            raise NotImplemented

    elif args.mode is None or args.mode == "gui":
        # GUI mode
        bp_dir = "."
        ftd_path = "."
        output_dir = "."

        try:
            with open("./path_defaults") as path_defaults:
                bp_dir, ftd_path, output_dir = map(str.strip, path_defaults.readlines())
        except:
            pass

        ftd_path = askdirectory(
            initialdir=ftd_path, mustexist=True, title="Path to FtD"
        )
        if not ftd_path:
            exit()

        try:
            ftd = ftd_dir(ftd_path)
        except Exception as e:
            showerror("FtD path error", message=str(e))
            exit()

        bp_path = askopenfilename(
            filetypes=[("Blueprint", ".blueprint")],
            initialdir=bp_dir,
            title="Blueprint to convert",
        )
        if not bp_path:
            exit()

        bp_path = Path(bp_path)

        output = asksaveasfilename(
            filetypes=[("Blueprint", ".blueprint")],
            initialdir=output_dir,
            title="Where to save converted blueprint?",
        )
        if not output:
            exit()
        output = FileType("w")(output)

        debeamify = askyesno("Mode select", "Do we want to debeamify craft instead?")

        if not debeamify:
            grains = []
            if askyesno(
                "Grain",
                "Is your craft primarily attacked from the front or back (frontsider)",
            ):
                grains.append("z")
            elif askyesno(
                "Grain",
                "Is your craft primarily attacked from the left or right (broadsider)",
            ):
                grains.append("x")
            elif askyesno(
                "Grain", "Is your craft primarily attacked from above/below? (topsider)"
            ):
                grains.append("y")
            else:
                showerror("What?")
                exit()

            if "z" not in grains and askyesno(
                "Grain",
                "Do you expect your craft to also possibly be attacked from the front or back?",
            ):
                grains.append("z")
            elif "x" not in grains and askyesno(
                "Grain",
                "Do you expect your craft to also possibly be attacked from the left or right?",
            ):
                grains.append("x")
            elif "y" not in grains and askyesno(
                "Grain",
                "Do you expect your craft to also possibly be attacked from the top or bottom",
            ):
                grains.append("y")
            else:
                showerror("Secondary attack direction must be chosen")
                exit()

            grains.extend({*"xyz"} - {*grains})
            grains.reverse()
            grain = "".join(grains)

            bias = "random"
            if askyesno(
                "Bias selection",
                "Do you want beams generally placed in a consistent order with shorter beams on one side? This will cause one side to be slightly weaker HP-wise than the other, but looks nicer.",
            ):
                bias = "sided"
            elif askyesno(
                "Bias selection",
                "Do you want beams generally placed in a consistent order, but alternating? This doesn't look nearly as nice, but HP will be more or less consistent.",
            ):
                bias = "alternate"

            do_exclude_4m = askyesno(
                "Exclusion criteria",
                "Do you want to exclude beams that are already 4m from beamification?",
            )
        else:
            grain = "xyz"
            bias = "random"
            do_exclude_4m = False

        color_exclusion = askstring(
            "Color exclusion",
            "If you want to exclude blocks of specific colors, specify these color numbers, comma-separated like 0, 29, 31.",
        )
        try:
            excluded_colors = color_string(color_exclusion)
        except:
            showerror(message="Invalid color exclusion string")
            exit()

        with open("./path_defaults", "w") as path_defaults:
            path_defaults.writelines(
                [
                    f"{bp_path.parent}\n",
                    f"{ftd.parent.parent}\n",
                    f"{Path(output.name).parent}",
                ]
            )

    guid_map = get_guid_map(ftd)

    bp, blocks, color_map = parse_blueprint(
        bp_path,
        guid_map,
        with_subconstructs=False,
    )

    s_field = construct_s_field(blocks, do_exclude_4m, excluded_colors)
    result = beamify(
        s_field=s_field, grain_directions=grain, bias_type=bias, debeamify=debeamify
    )
    output.write(
        make_bp_from_field(field=result, guid_map=guid_map, blocks=blocks, og_bp=bp)
    )
