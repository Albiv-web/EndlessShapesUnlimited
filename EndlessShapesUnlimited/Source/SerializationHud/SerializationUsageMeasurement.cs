using System;
using Assets.Scripts;

namespace DecoLimitLifter.SerializationHud
{
    internal static class SerializationUsageMeasurement
    {
        internal static CraftSerializationSnapshot Measure(MainConstruct construct)
        {
            if (construct == null)
                throw new ArgumentNullException(nameof(construct));

            using (SerializationTelemetryOperation operation =
                   SerializationTelemetry.Begin(SerializationOperationKind.Measure, construct))
            {
                global::Blueprint blueprint = BlueprintConverter.Convert(
                    construct,
                    false);
                operation.RecordBlueprintUsage(blueprint);
                operation.Complete(construct);
            }

            SerializationTelemetry.TryGetHistory(
                construct,
                out _,
                out _,
                out CraftSerializationSnapshot measured);
            return measured;
        }
    }
}
