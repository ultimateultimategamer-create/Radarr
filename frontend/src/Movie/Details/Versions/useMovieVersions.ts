import { useCallback, useState } from 'react';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import MovieVersion from './MovieVersion';

function useMovieVersions(movieId: number) {
  const [removeError, setRemoveError] = useState<string | null>(null);
  const { data, refetch } = useApiQuery<MovieVersion[]>({
    path: `/movieversion?movieId=${movieId}`,
  });

  const removeVersion = useCallback(
    (id: number) => {
      setRemoveError(null);
      const { request } = createAjaxRequest({
        url: `/movieversion/${id}`,
        method: 'DELETE',
        dataType: 'json',
      });

      request.always(() => {
        refetch();
      });
      request.fail((xhr: { responseJSON?: { message?: string } }) => {
        setRemoveError(xhr.responseJSON?.message ?? 'Unable to remove version');
      });
    },
    [refetch]
  );

  return {
    versions: data ?? [],
    refetchVersions: refetch,
    removeVersion,
    removeError,
  };
}

export default useMovieVersions;
