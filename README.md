# LearningTPLDataFlow
This repository captures my process of learning the TPL DataFlow framework. The idea is to commit some code, ask questions on the Stack Overflow and then implement the feedback.

The project depends on Microsoft Build API, but not the one distributed as NuGet package, but the one being part of the Visual Studio installation. This is not what I wanted,
but making NuGet work is too much of a pain - https://stackoverflow.com/questions/66146972/whats-the-correct-usage-of-microsoft-build-evaluation-fast-forward-to-2021

## find-string
Finds the given string literal in the C# code across all the projects in the solutions listed in the Solution List file.
Suppose all our C# code is under the folder called workspace root. Then the code assumes that the solution list file is `build\projects.yml` under that root. This mimics
the structure of the code in my own workspace and makes it simpler for me to test the example code.

For example, my current `build\project.yml` file is:
```
parameters:
  - name: buildTemplate

extends:
  template: ${{ parameters.buildTemplate }}
  parameters:
    projects:
      - name: src\Architecture\Architecture.DfVersioning
        shortName: DfVersioning
      - name: DataSvc
      - name: TSMain
        noRestore: true
        noTest: true
      - name: Main
      - name: HcmAnywhere
      - name: SoftClock
      - name: BI\BI
        shortName: BI
      - name: DFAcceptanceTest
      - name: Build\Deployer
        shortName: Deployer
```
So, the code reads this file, extracts the solution paths, parses the solutions, extracts the project paths and parses them using Microsoft Build API.
