using System;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Avatar.Build.UndoRedo;

namespace DecoLimitLifter.SmartBuildMode
{
    internal interface ISmartBuildCommandFactory
    {
        ICommand CreateRemoval(AllConstruct construct, SmartBuildRemovalItem removal);

        ICommand CreatePlacement(AllConstruct construct, SmartBuildPlacement placement);
    }

    internal interface ISmartBuildUndoRegistrar
    {
        void Register(ICommand command);
    }

    internal sealed class SmartBuildNativeCommandFactory : ISmartBuildCommandFactory
    {
        internal static SmartBuildNativeCommandFactory Instance { get; } =
            new SmartBuildNativeCommandFactory();

        private SmartBuildNativeCommandFactory()
        {
        }

        public ICommand CreateRemoval(
            AllConstruct construct,
            SmartBuildRemovalItem removal) =>
            new RemoveBlockCommand(
                construct ?? throw new ArgumentNullException(nameof(construct)),
                removal?.CommandCell ??
                    throw new ArgumentNullException(nameof(removal)),
                MirrorInfo.none);

        public ICommand CreatePlacement(
            AllConstruct construct,
            SmartBuildPlacement placement) =>
            new PlaceBlockCommand(
                construct ?? throw new ArgumentNullException(nameof(construct)),
                placement?.Position ??
                    throw new ArgumentNullException(nameof(placement)),
                placement.Rotation,
                placement.Candidate?.Definition ??
                    throw new InvalidOperationException(
                        "The planned block definition is unavailable."),
                0,
                MirrorInfo.none);
    }

    internal sealed class SmartBuildDelegateUndoRegistrar : ISmartBuildUndoRegistrar
    {
        private readonly Action<ICommand> _register;

        internal SmartBuildDelegateUndoRegistrar(Action<ICommand> register)
        {
            _register = register ?? throw new ArgumentNullException(nameof(register));
        }

        public void Register(ICommand command) =>
            _register(command ?? throw new ArgumentNullException(nameof(command)));
    }
}
