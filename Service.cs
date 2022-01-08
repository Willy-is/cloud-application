using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using System.IO;
using System.Net;
using AT1;
using System.Diagnostics;

public class Service : IService
{
    public String GenerateAllocations(String configurationFileName)
    {
        Configuration AT1Configuration;
        String result;

        // Parse Configuration file.
        using (WebClient configurationClient = new WebClient())
        using (Stream configurationStream = configurationClient.OpenRead(configurationFileName))
        using (StreamReader configurationFile = new StreamReader(configurationStream))
        {
            Configuration.TryParse(configurationFile, configurationFileName, out AT1Configuration, out List<String> configurationErrors);
        }

        // Generate one set of Allocations (taff format) by greedy algorithm.
        String TAFF = GreedyAlgorithm(AT1Configuration);

        Allocations.TryParse(TAFF, AT1Configuration, out Allocations AT1Allocations, out List<String> allocationsErrors);

        // If a set of allocation found.
        if (allocationsErrors.Count == 0)
            result = TAFF;
        else
            result = ZeroAllocations(AT1Configuration);

        return (result);
    }

    private String GreedyAlgorithm(Configuration AT1Configuration)
    {
        Double[,] runtimes = new Double[AT1Configuration.NumberOfProcessors, AT1Configuration.NumberOfTasks];
        int[,] map = new int[AT1Configuration.NumberOfProcessors, AT1Configuration.NumberOfTasks];
        int[,] allocation = new int[AT1Configuration.NumberOfProcessors, AT1Configuration.NumberOfTasks];

        // Check whether all the tasks assigned to the processors.
        int numberOfTasksAllocated = 0;

        // Put runtimes in a 2D array.
        for (int row = 0; row < AT1Configuration.NumberOfProcessors; row++)
        {
            for (int column = 0; column < AT1Configuration.NumberOfTasks; column++)
            {
                runtimes[row, column] = AT1Configuration.Runtimes[row * AT1Configuration.NumberOfTasks + column];
            }
        }

        // Block the cell where taskRAM > procRAM.
        for (int row = 0; row < AT1Configuration.NumberOfProcessors; row++)
        {
            for (int column = 0; column < AT1Configuration.NumberOfTasks; column++)
            {
                if (AT1Configuration.TaskRAM[column] > AT1Configuration.ProcessorRAM[row])
                    map[row, column] = 1;
            }
        }

        // Block random cells for each VM.
        Randomise(map, AT1Configuration.NumberOfProcessors, AT1Configuration.NumberOfTasks);

        // Temporary 1D array for processors to indicate the current sum values.
        Double[] totalRuntimes = new Double[AT1Configuration.NumberOfProcessors];

        // Temporary 2D array to calculate the sum of runtimes in each processor.
        Double[,] allocatedRuntimes = new Double[AT1Configuration.NumberOfProcessors, AT1Configuration.NumberOfTasks];

        // Check time elapsed in Server side. If error occur, client catch the exception
        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();
        int Deadline = 300000;

        // Check column by column, start at the last column, assign the largest runtime task if available.
        for (int column = AT1Configuration.NumberOfTasks - 1; column > -1; column--)
        {
            if (stopwatch.ElapsedMilliseconds > Deadline)
            {
                stopwatch.Stop();

                throw new TimeoutException("WCF Service timed out");
            }

            for (int row = 0; row < AT1Configuration.NumberOfProcessors; row++)
            {
                if (map[row, column] == 0 && runtimes[row, column] + totalRuntimes[row] <= AT1Configuration.MaximumProgramDuration)
                {
                    allocatedRuntimes[row, column] = runtimes[row, column];
                }
                else
                    allocatedRuntimes[row, column] = 0;
            }

            // Find the largest runtime in each column, and associated processor.
            Double largestRuntime = 0;
            int processorNumber = 0;

            for (int row = 0; row < AT1Configuration.NumberOfProcessors; row++)
            {
                if (allocatedRuntimes[row, column] > largestRuntime)
                {
                    largestRuntime = allocatedRuntimes[row, column];
                    processorNumber = row;
                }
            }

            // Allocate the largest runtime Task[column] to Processors[processorNumber].
            if (largestRuntime == 0)
                allocation[processorNumber, column] = 0;
            else
            {
                allocation[processorNumber, column] = 1;
                totalRuntimes[processorNumber] += allocatedRuntimes[processorNumber, column];
                numberOfTasksAllocated++;
            }

            // Reset rest of runtimes for next round.
            for (int row = 0; row < AT1Configuration.NumberOfProcessors; row++)
            {
                if (row != processorNumber)
                    allocatedRuntimes[row, column] = 0;
            }
        }

        // Convert it to taff file format
        String TAFF = "";
        if (numberOfTasksAllocated == AT1Configuration.NumberOfTasks)
            TAFF = TaffFileFormat(allocation, AT1Configuration);

        return (TAFF);
    }

    private String TaffFileFormat(int[,] allocation, Configuration AT1Configuration)
    {
        // Basic format before allocating
        String TAFF = "";
        TAFF += @"CONFIG-FILE=""" + AT1Configuration.FilePath + @"""" + Environment.NewLine;
        TAFF += @"ALLOCATIONS-DATA=1," + AT1Configuration.NumberOfTasks + @"," + AT1Configuration.NumberOfProcessors + Environment.NewLine;
        TAFF += @"ALLOCATION-ID=1" + Environment.NewLine;

        for (int row = 0; row < AT1Configuration.NumberOfProcessors; row++)
        {
            TAFF += @"";
            for (int column = 0; column < AT1Configuration.NumberOfTasks; column++)
            {
                if (column < AT1Configuration.NumberOfTasks - 1)
                    TAFF += allocation[row, column] + @",";
                else
                    TAFF += allocation[row, column] + @"" + Environment.NewLine;
            }
        }

        return (TAFF);
    }

    private String ZeroAllocations(Configuration AT1Configuration)
    {
        String TAFF = "";
        TAFF += @"CONFIG-FILE=""" + AT1Configuration.FilePath + @"""" + Environment.NewLine;
        TAFF += @"ALLOCATIONS-DATA=0," + AT1Configuration.NumberOfTasks + @"," + AT1Configuration.NumberOfProcessors + Environment.NewLine;

        return (TAFF);
    }

    private void Randomise(int[,] map, int rows, int columns)
    {
        Random random = new Random((int)DateTime.Now.Ticks);

        for (int column = 0; column < columns; column++)
        {
            int count = 0;

            while (count < 1)
            {
                int randomRow = random.Next(0, rows);

                if (map[randomRow, column] == 0)
                {
                    map[randomRow, column] = 1;
                    count++;
                }
            }
        }
    }
}
