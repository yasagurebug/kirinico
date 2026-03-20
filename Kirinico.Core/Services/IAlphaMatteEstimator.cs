using Kirinico.Core.Models;
using OpenCvSharp;

namespace Kirinico.Core.Services;

public interface IAlphaMatteEstimator : IDisposable
{
    Mat? EstimateAlpha(Mat referenceBgr, Mat trimapMask, MattingMethod method, MattingSettings settings);

    void CancelCurrentRequest();
}
