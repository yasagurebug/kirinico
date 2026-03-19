using Kirinico.Core.Models;
using OpenCvSharp;

namespace Kirinico.Core.Services;

public interface IAlphaMatteEstimator : IDisposable
{
    Mat? EstimateAlpha(Mat referenceBgr, Mat trimapMask, MattingSettings settings);

    void CancelCurrentRequest();
}
